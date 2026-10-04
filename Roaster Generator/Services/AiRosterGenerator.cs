using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class AiRosterResponseException(string message) : Exception(message);

/// <summary>
/// Uses OpenAI function calling to retrieve the generation inputs and propose a driver roster.
/// Every proposed roster is still checked by the deterministic roster validator before persistence.
/// </summary>
public sealed class AiRosterGenerator(HttpClient httpClient, IConfiguration configuration)
{
    private const string DefaultModel = "gpt-6-astra";
    private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredDataTools =
    [
        "get_weekly_demand",
        "get_driver_target_hours",
        "get_driver_availability",
        "get_fairness_across_previous_weeks",
        "get_driver_constraints"
    ];

    public string Model => configuration["OPENAI_MODEL"]?.Trim() is { Length: > 0 } model
        ? model
        : DefaultModel;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["OPENAI_API_KEY"]);

    public async Task<IReadOnlyList<RosterSolverShift>> ProposeAsync(
        LoadedRosterInput loaded,
        IReadOnlyList<string>? validationFeedback,
        CancellationToken ct)
    {
        var apiKey = configuration["OPENAI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new RosterInputException("AI roster generation is unavailable because OPENAI_API_KEY is not configured on the server.");

        var codeByEmployee = loaded.Input.Employees.OrderBy(employee => employee.Id)
            .Select((employee, index) => (employee.Id, Code: $"D{index + 1:000}"))
            .ToDictionary(item => item.Id, item => item.Code);
        var employeeByCode = codeByEmployee.ToDictionary(item => item.Value, item => item.Key, StringComparer.Ordinal);
        var tools = DataTools();
        var input = new List<JsonNode?>
        {
            JsonNode.Parse(JsonSerializer.Serialize(new
            {
                role = "user",
                content = CreatePrompt(loaded, AnonymizeFeedback(loaded, validationFeedback))
            }))
        };
        var calledTools = new HashSet<string>(StringComparer.Ordinal);

        for (var round = 0; round < 7 && !RequiredDataTools.All(calledTools.Contains); round++)
        {
            using var response = await SendResponseAsync(input, tools, "required", apiKey, ct);
            var outputs = GetOutput(response.RootElement);
            AppendResponseOutput(input, outputs);

            foreach (var call in outputs.Where(IsFunctionCall))
            {
                var name = call.GetProperty("name").GetString() ?? string.Empty;
                if (!RequiredDataTools.Contains(name, StringComparer.Ordinal))
                    throw new RosterInputException("The AI requested an unknown roster data function.");

                var data = GetToolData(name, loaded.Input, codeByEmployee);
                calledTools.Add(name);
                input.Add(FunctionOutput(call, JsonSerializer.Serialize(data, WebJson)));
            }
        }

        if (!RequiredDataTools.All(calledTools.Contains))
            throw new RosterInputException("The AI did not retrieve all required demand, availability, target-hours and fairness data. No roster was saved.");

        using var proposalResponse = await SendResponseAsync(
            input, [ProposalTool()], new { type = "function", name = "propose_roster" }, apiKey, ct);
        var proposalCall = GetOutput(proposalResponse.RootElement).FirstOrDefault(item =>
            IsFunctionCall(item) && item.GetProperty("name").GetString() == "propose_roster");
        if (proposalCall.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new AiRosterResponseException($"OpenAI response {ResponseId(proposalResponse.RootElement)} completed without a structured roster proposal.");

        EnsureFunctionCallCompleted(proposalCall, proposalResponse.RootElement);
        if (!proposalCall.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.String)
            throw new AiRosterResponseException($"OpenAI response {ResponseId(proposalResponse.RootElement)} did not contain complete roster arguments.");

        AiRosterProposal proposal;
        try
        {
            proposal = JsonSerializer.Deserialize<AiRosterProposal>(arguments.GetString() ?? "{}", WebJson)
                ?? throw new AiRosterResponseException($"OpenAI response {ResponseId(proposalResponse.RootElement)} returned an empty roster proposal.");
        }
        catch (JsonException)
        {
            throw new AiRosterResponseException($"OpenAI response {ResponseId(proposalResponse.RootElement)} contained incomplete or malformed roster JSON.");
        }

        var shifts = new List<RosterSolverShift>(proposal.Shifts.Count);
        foreach (var shift in proposal.Shifts)
        {
            if (!employeeByCode.TryGetValue(shift.EmployeeCode, out var employeeId) ||
                !DateOnly.TryParseExact(shift.Date, "yyyy-MM-dd", out var date))
                throw new RosterInputException("The AI roster contains an unknown driver code or invalid date. No roster was saved.");

            shifts.Add(new RosterSolverShift(employeeId, date, shift.StartHour, shift.FinishHour));
        }

        return shifts;
    }

    private async Task<JsonDocument> SendResponseAsync(
        IReadOnlyList<JsonNode?> input,
        object[] tools,
        object toolChoice,
        string apiKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = Model,
            instructions = "You are a roster planner. Use only the application functions for factual scheduling inputs. Never invent employees, demand, targets, availability or history. Availability means a driver is able to work during that window; it is not a request or obligation to schedule them.",
            input,
            tools,
            tool_choice = toolChoice,
            parallel_tool_calls = true,
            background = true,
            store = false,
            max_output_tokens = 9000
        }), Encoding.UTF8, "application/json");

        JsonDocument? current = null;
        string? responseId = null;
        try
        {
            using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                current = await ReadResponseDocumentAsync(response, ct);
            }

            responseId = ResponseId(current.RootElement);
            while (IsBackgroundResponseInProgress(current.RootElement))
            {
                await Task.Delay(BackgroundPollInterval, ct);
                using var pollRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.openai.com/v1/responses/{Uri.EscapeDataString(responseId)}");
                pollRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var pollResponse = await httpClient.SendAsync(pollRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                var next = await ReadResponseDocumentAsync(pollResponse, ct);
                current.Dispose();
                current = next;
            }

            EnsureResponseCompleted(current.RootElement);
            var completed = current;
            current = null;
            return completed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (responseId is not null)
                await TryCancelBackgroundResponseAsync(responseId, apiKey);
            throw;
        }
        finally { current?.Dispose(); }
    }

    private async Task<JsonDocument> ReadResponseDocumentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new RosterInputException($"OpenAI roster generation failed with HTTP {(int)response.StatusCode}. Check the server API-key configuration and model access; no roster was saved.");

        var body = await response.Content.ReadAsStringAsync(ct);
        try { return JsonDocument.Parse(body); }
        catch (JsonException)
        {
            throw new AiRosterResponseException("OpenAI returned an incomplete or malformed response body.");
        }
    }

    private static bool IsBackgroundResponseInProgress(JsonElement response) =>
        response.TryGetProperty("status", out var status) &&
        status.GetString() is "queued" or "in_progress";

    private async Task TryCancelBackgroundResponseAsync(string responseId, string apiKey)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.openai.com/v1/responses/{Uri.EscapeDataString(responseId)}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Cancellation is best-effort; preserve the original local cancellation either way.
        }
    }

    private static void EnsureResponseCompleted(JsonElement response)
    {
        var status = response.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (status == "completed") return;

        var reason = response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object &&
                     details.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;
        throw new AiRosterResponseException(
            $"OpenAI response {ResponseId(response)} did not complete (status: {status ?? "missing"}, reason: {reason ?? "unspecified"}).");
    }

    private static void EnsureFunctionCallCompleted(JsonElement functionCall, JsonElement response)
    {
        if (!functionCall.TryGetProperty("status", out var statusElement) || statusElement.GetString() == "completed") return;
        throw new AiRosterResponseException(
            $"OpenAI response {ResponseId(response)} returned a roster function call that did not complete (status: {statusElement.GetString() ?? "missing"}).");
    }

    private static string ResponseId(JsonElement response) =>
        response.TryGetProperty("id", out var id) ? id.GetString() ?? "unknown" : "unknown";

    private static object[] DataTools() =>
    [
        DataTool("get_weekly_demand", "Read the selected week's required driver count for each open business hour."),
        DataTool("get_driver_target_hours", "Read each driver's automatically calculated approximate target hours and useful capacity."),
        DataTool("get_driver_availability", "Read when each driver is able to work. Availability is permission, not a commitment to work."),
        DataTool("get_fairness_across_previous_weeks", "Read saved hours compared with approximate targets for each of the previous four weeks."),
        DataTool("get_driver_constraints", "Read each driver's maximum weekly hours, vehicle type and ownership, company fleet limits, and generation rest/start settings.")
    ];

    private static object DataTool(string name, string description) => new
    {
        type = "function",
        name,
        description,
        parameters = new
        {
            type = "object",
            properties = new Dictionary<string, object>(),
            required = Array.Empty<string>(),
            additionalProperties = false
        },
        strict = true
    };

    private static object ProposalTool() => new
    {
        type = "function",
        name = "propose_roster",
        description = "Submit the complete proposed driver roster for the selected week. Use employee codes returned by the data functions and business-day absolute hour values.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                shifts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_code = new { type = "string" },
                            date = new { type = "string", description = "Business date in yyyy-MM-dd format." },
                            start_hour = new { type = "integer", description = "Business-day hour; valid starts are 6 through 29." },
                            finish_hour = new { type = "integer", description = "Business-day finish hour; must be greater than start and produce a 3–10 hour shift." }
                        },
                        required = new[] { "employee_code", "date", "start_hour", "finish_hour" },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "shifts" },
            additionalProperties = false
        },
        strict = true
    };

    private static object GetToolData(
        string name,
        RosterSolverInput input,
        IReadOnlyDictionary<Guid, string> codeByEmployee) => name switch
    {
        "get_weekly_demand" => new
        {
            weekStart = input.WeekStart.ToString("yyyy-MM-dd"),
            hours = input.Demand.Select(slot => new
            {
                date = slot.Date.ToString("yyyy-MM-dd"),
                businessHour = slot.Hour,
                requiredDrivers = slot.RequiredDrivers
            })
        },
        "get_driver_target_hours" => new
        {
            drivers = input.Employees.Select(employee => new
            {
                employeeCode = codeByEmployee[employee.Id],
                approximateTargetHours = input.ExpectedHoursByEmployee?.GetValueOrDefault(employee.Id)?.ExpectedHours ?? 0,
                usefulCapacityHours = input.ExpectedHoursByEmployee?.GetValueOrDefault(employee.Id)?.CapacityHours ?? 0
            })
        },
        "get_driver_availability" => new
        {
            availabilityMeans = "Can work in the listed interval; it is not a commitment or instruction to schedule the driver.",
            drivers = input.Employees.Select(employee => new
            {
                employeeCode = codeByEmployee[employee.Id],
                windows = input.Availability.Where(window => window.EmployeeId == employee.Id)
                    .Select(ToAvailabilityWindow)
                    .Select(window => new
                    {
                        date = window.Date.ToString("yyyy-MM-dd"),
                        startHour = window.Start,
                        finishHour = window.Finish
                    })
            })
        },
        "get_fairness_across_previous_weeks" => new
        {
            weeks = (input.History ?? []).OrderBy(item => item.WeekStart).Select(item => new
            {
                employeeCode = codeByEmployee.GetValueOrDefault(item.EmployeeId),
                weekStart = item.WeekStart.ToString("yyyy-MM-dd"),
                scheduledHours = item.ScheduledHours,
                approximateTargetHours = item.ApproximateHours,
                targetUsePercent = item.ApproximateHours > 0
                    ? Math.Round(item.ScheduledHours * 100d / item.ApproximateHours, 1)
                    : (double?)null,
                shiftCount = item.ShiftCount
            })
        },
        "get_driver_constraints" => new
        {
            generation = new
            {
                minimumShiftHours = 3,
                preferredMinimumShiftHours = 5,
                maximumShiftHours = 10,
                latestShiftStartHour = input.Options.LatestShiftStartHour,
                minimumRestHours = input.Options.MinimumRestHours,
                preferredRestHours = input.Options.PreferredRestHours,
                companyCars = input.Options.CompanyCars,
                companyMopeds = input.Options.CompanyMopeds,
                companyEBikes = input.Options.CompanyEBikes,
                eBikeNeedsCarOrMopedAlongside = true,
                maxOneShiftPerDriverPerBusinessDay = true
            },
            drivers = input.Employees.Select(employee => new
            {
                employeeCode = codeByEmployee[employee.Id],
                maximumWeeklyHours = employee.MaximumWeeklyHours,
                vehicleType = employee.DriverProfile?.DriverType.ToString(),
                usesOwnVehicle = employee.DriverProfile?.IsOwn ?? true
            })
        },
        _ => throw new RosterInputException("The AI requested an unknown roster data function.")
    };

    private static (DateOnly Date, int Start, int Finish) ToAvailabilityWindow(Shift shift)
    {
        var start = shift.StartTime.Hour < 6 ? shift.StartTime.Hour + 24 : shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        while (finish <= start) finish += 24;
        return (shift.Date, start, finish);
    }

    private static string CreatePrompt(LoadedRosterInput loaded, IReadOnlyList<string>? validationFeedback)
    {
        var feedback = validationFeedback is { Count: > 0 }
            ? $"\nYour previous proposal failed validation. Correct every issue before proposing again:\n- {string.Join("\n- ", validationFeedback)}"
            : string.Empty;
        return $"Create a complete driver roster for the week starting {loaded.Input.WeekStart:yyyy-MM-dd}. Before proposing, call every available read function: weekly demand, driver target hours, availability, previous-week fairness, and driver constraints.\n" +
               "Schedule demand only, and never schedule more drivers than the required count in any hour. Maximize covered driver-hours; understaff only when the available employees and hard constraints make full coverage impossible. Use each driver's approximate target and prior-week target-use percentages to distribute work fairly.\n" +
               "Availability means the driver can work during that interval; it does not mean they must work that day or hour. Do not fill every available interval automatically.\n" +
               "Use continuous shifts, with a hard length of 3–10 hours. Maximize average hours per shift and prefer shifts of at least 5 hours. Use 3–4 hour shifts only when coverage, availability, demand shape, rest, or vehicle constraints make longer shifts unsuitable. Do not extend a shift into an hour with zero demand.\n" +
               "Respect each driver's availability for the full shift, latest allowed start, minimum rest, at most one shift per driver per business day, company vehicle counts, e-bike support, and maximum weekly hours as a fairness/capacity ceiling where possible. Demand coverage takes priority over approximate targets if needed.\n" +
               "Return all shifts through propose_roster using the anonymous employee codes from the data functions. Dates are business dates. Hours use the business-day convention: 06–23 are 6–23, then next-day 00–05 are 24–29. Finish hour is exclusive. Never invent a driver or omit required fields." + feedback;
    }

    private static IReadOnlyList<string>? AnonymizeFeedback(LoadedRosterInput loaded, IReadOnlyList<string>? feedback)
    {
        if (feedback is null) return null;
        var names = loaded.Input.Employees
            .OrderBy(employee => employee.Id)
            .Select((employee, index) => (employee, code: $"D{index + 1:000}"))
            .ToArray();
        return feedback.Select(message =>
        {
            foreach (var (employee, code) in names)
            {
                message = message.Replace($"{employee.FirstName} {employee.LastName}", code, StringComparison.OrdinalIgnoreCase)
                    .Replace(employee.Id.ToString(), code, StringComparison.OrdinalIgnoreCase);
            }
            return message;
        }).ToArray();
    }

    private static JsonElement[] GetOutput(JsonElement response) =>
        response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array
            ? output.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [];

    private static bool IsFunctionCall(JsonElement item) =>
        item.TryGetProperty("type", out var type) && type.GetString() == "function_call";

    private static void AppendResponseOutput(List<JsonNode?> input, IReadOnlyList<JsonElement> output)
    {
        foreach (var item in output)
            input.Add(JsonNode.Parse(item.GetRawText()));
    }

    private static JsonNode FunctionOutput(JsonElement call, string output) => JsonNode.Parse(JsonSerializer.Serialize(new
    {
        type = "function_call_output",
        call_id = call.GetProperty("call_id").GetString(),
        output
    }))!;

    private sealed class AiRosterProposal
    {
        [JsonPropertyName("shifts")]
        public List<AiRosterShift> Shifts { get; init; } = [];
    }

    private sealed class AiRosterShift
    {
        [JsonPropertyName("employee_code")]
        public string EmployeeCode { get; init; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; init; } = string.Empty;

        [JsonPropertyName("start_hour")]
        public int StartHour { get; init; }

        [JsonPropertyName("finish_hour")]
        public int FinishHour { get; init; }
    }
}
