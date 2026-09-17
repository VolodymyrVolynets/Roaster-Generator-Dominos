using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public interface IFairDriverHoursCalculator
{
    FairDriverHoursResult Calculate(
        IReadOnlyList<Employee> drivers,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<RosterSolverDemand> demand,
        double alpha = 0.7,
        string rosterKind = RosterKinds.Drivers,
        CompanyVehicleFleet? fleet = null,
        IReadOnlyList<RosterSolverBoundaryShift>? boundaries = null);
}

public sealed record FairDriverHoursAllocation(
    Guid EmployeeId,
    double ExpectedHours,
    double CapacityHours,
    double RawScore,
    double FairScore);

public sealed record FairDriverHoursResult(
    double TotalDemandHours,
    double TotalAllocatedHours,
    double UnallocatedDemandHours,
    IReadOnlyDictionary<Guid, FairDriverHoursAllocation> Drivers);

public sealed class FairDriverHoursCalculator : IFairDriverHoursCalculator
{
    private const double Epsilon = 0.000_001;

    public FairDriverHoursResult Calculate(
        IReadOnlyList<Employee> drivers,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<RosterSolverDemand> demand,
        double alpha = 0.7,
        string rosterKind = RosterKinds.Drivers,
        CompanyVehicleFleet? fleet = null,
        IReadOnlyList<RosterSolverBoundaryShift>? boundaries = null)
    {
        if (!double.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha), "Fairness alpha must be between 0 and 1.");
        if (drivers.Any(employee => employee.MaximumWeeklyHours is < 0 or > 168))
            throw new ArgumentOutOfRangeException(nameof(drivers), "Maximum weekly hours must be between 0 and 168.");

        RosterKinds.EnsureEnabled(rosterKind);
        fleet ??= new CompanyVehicleFleet();
        boundaries ??= [];
        if (CompanyVehicleFleet.Types.Any(type => fleet.Count(type) is < 0 or > 1000))
            throw new ArgumentOutOfRangeException(nameof(fleet), "Company vehicle counts must be between 0 and 1000.");
        var eligible = drivers.Where(employee => employee.IsActive &&
                (rosterKind == RosterKinds.Inside
                    ? employee.ManagerProfile is not null || employee.InStoreProfile is not null
                    : employee.DriverProfile is not null))
            .DistinctBy(driver => driver.Id)
            .OrderBy(driver => driver.Id)
            .ToArray();
        var eligibleIds = eligible.Select(driver => driver.Id).ToHashSet();
        var windows = availability
            .Where(item => eligibleIds.Contains(item.EmployeeId))
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Select(ToWindow).ToArray());
        var demandedSlots = demand.Where(slot => slot.RequiredDrivers > 0)
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.Hour)
            .ToArray();
        var rawScores = eligible.ToDictionary(driver => driver.Id, _ => 0d);
        var usefulSlots = eligible.ToDictionary(driver => driver.Id,
            _ => new HashSet<(DateOnly Date, int Hour)>());
        var coverableDemand = 0d;
        var hourlyDrivers = new List<(RosterSolverDemand Slot, Employee[] Drivers)>();

        foreach (var slot in demandedSlots)
        {
            var available = eligible.Where(driver => driver.MaximumWeeklyHours > 0 && IsAvailable(
                windows.GetValueOrDefault(driver.Id) ?? [], slot.Date, slot.Hour)).ToArray();
            if (rosterKind == RosterKinds.Drivers)
                available = fleet.UsableDrivers(available, slot.RequiredDrivers,
                    slot.Date.ToDateTime(TimeOnly.MinValue).AddHours(slot.Hour), boundaries);
            if (available.Length == 0) continue;

            hourlyDrivers.Add((slot, available));

            coverableDemand += Math.Min(slot.RequiredDrivers, available.Length);
            var slotWeight = slot.RequiredDrivers / (double)available.Length;
            foreach (var driver in available)
            {
                rawScores[driver.Id] += slotWeight;
                usefulSlots[driver.Id].Add((slot.Date, slot.Hour));
            }
        }

        var capacities = eligible.ToDictionary(driver => driver.Id, driver => Math.Min((double)driver.MaximumWeeklyHours,
            usefulSlots[driver.Id].GroupBy(slot => slot.Date)
                .Sum(day => Math.Min(10, day.Count()))));
        var fairScores = eligible.ToDictionary(driver => driver.Id, driver =>
            rawScores[driver.Id] <= Epsilon ? 0 : alpha == 0 ? 1 : Math.Pow(rawScores[driver.Id], alpha));
        var expected = eligible.ToDictionary(driver => driver.Id, _ => 0d);
        var totalDemand = demandedSlots.Sum(slot => (double)slot.RequiredDrivers);
        // Weekly shares alone can count a shared vehicle twice. First find the maximum
        // fractional hours that fit actual availability, fleet, support and employee caps.
        using var hourlyAllocation = rosterKind == RosterKinds.Drivers
            ? new HourlyDriverAllocation(eligible, hourlyDrivers, fleet, boundaries) : null;
        var distributable = hourlyAllocation?.MaximumHours() ?? Math.Min(coverableDemand, capacities.Values.Sum());
        var remaining = hourlyAllocation is null ? distributable : 0;

        while (remaining > Epsilon)
        {
            var recipients = eligible.Where(driver => fairScores[driver.Id] > Epsilon &&
                capacities[driver.Id] - expected[driver.Id] > Epsilon).ToArray();
            if (recipients.Length == 0) break;

            var scoreTotal = recipients.Sum(driver => fairScores[driver.Id]);
            var distributed = 0d;
            foreach (var driver in recipients)
            {
                var share = remaining * fairScores[driver.Id] / scoreTotal;
                var allocation = Math.Min(share, capacities[driver.Id] - expected[driver.Id]);
                expected[driver.Id] += allocation;
                distributed += allocation;
            }

            if (distributed <= Epsilon) break;
            remaining -= distributed;
        }

        if (hourlyAllocation is not null)
            expected = hourlyAllocation.AllocateFairly(fairScores, capacities, distributable);

        var allocations = eligible.ToDictionary(driver => driver.Id, driver => new FairDriverHoursAllocation(
            driver.Id,
            Round(expected[driver.Id]),
            Round(capacities[driver.Id]),
            Round(rawScores[driver.Id]),
            Round(fairScores[driver.Id])));
        var allocated = expected.Values.Sum();
        return new FairDriverHoursResult(
            Round(totalDemand),
            Round(allocated),
            Round(Math.Max(0, totalDemand - allocated)),
            allocations);
    }

    private static AvailabilityWindow ToWindow(Shift shift)
    {
        var start = shift.StartTime.Hour < 6 ? shift.StartTime.Hour + 24 : shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        while (finish <= start) finish += 24;
        return new AvailabilityWindow(shift.Date, start, finish);
    }

    private static bool IsAvailable(IEnumerable<AvailabilityWindow> windows, DateOnly date, int hour) =>
        windows.Any(window => window.Date == date && window.StartHour <= hour && window.FinishHour > hour);

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record AvailabilityWindow(DateOnly Date, int StartHour, int FinishHour);
}
