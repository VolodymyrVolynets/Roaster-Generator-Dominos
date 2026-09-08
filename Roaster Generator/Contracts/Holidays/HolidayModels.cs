using Roaster_Generator.Enums;

namespace Roaster_Generator.Contracts.Holidays;

public sealed class HolidayHoursRequest
{
    public int Hours { get; set; }
}

public sealed class HolidayResponse
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public string EmployeeNumber { get; init; } = string.Empty;

    public string? PayrollNumber { get; init; }

    public int Hours { get; init; }

    public HolidayStatus Status { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? UsedAtUtc { get; init; }
}

public sealed class EmployeeHolidayResponse
{
    public HolidayResponse? Requested { get; init; }

    public IReadOnlyList<HolidayResponse> Used { get; init; } = [];
}

public sealed class AdminHolidayResponse
{
    public IReadOnlyList<HolidayResponse> Requested { get; init; } = [];

    public IReadOnlyList<HolidayResponse> Used { get; init; } = [];
}

public sealed class HolidayApprovalResponse
{
    public int ApprovedCount { get; init; }
}
