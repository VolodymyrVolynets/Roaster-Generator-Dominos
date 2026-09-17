using Google.OrTools.LinearSolver;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

/// <summary>
/// Fractional preview, not a shift roster. Maximizes useful hours, then finds the closest
/// feasible scarcity-weighted fair allocation without giving up coverage.
/// Whole shifts, minimum rest and shift continuity remain the roster solver's responsibility.
/// </summary>
internal sealed class HourlyDriverAllocation : IDisposable
{
    private readonly Solver solver = Solver.CreateSolver("GLOP")
        ?? throw new InvalidOperationException("The approximate-hours solver is unavailable.");
    private readonly Dictionary<Guid, List<Variable>> employeeHours;
    private readonly Constraint total;

    public HourlyDriverAllocation(IReadOnlyList<Employee> employees,
        IEnumerable<(RosterSolverDemand Slot, Employee[] Drivers)> slots,
        CompanyVehicleFleet fleet, IReadOnlyList<RosterSolverBoundaryShift> boundaries)
    {
        employeeHours = employees.ToDictionary(employee => employee.Id, _ => new List<Variable>());
        var weekly = employees.ToDictionary(employee => employee.Id,
            employee => solver.MakeConstraint(0, employee.MaximumWeeklyHours));
        var daily = new Dictionary<(Guid, DateOnly), Constraint>();
        total = solver.MakeConstraint(0, double.PositiveInfinity);

        foreach (var (slot, drivers) in slots)
        {
            var hour = slot.Date.ToDateTime(TimeOnly.MinValue).AddHours(slot.Hour);
            var demand = solver.MakeConstraint(0, slot.RequiredDrivers);
            var supportedTime = solver.MakeNumVar(0, 1, "");
            var support = solver.MakeConstraint(double.NegativeInfinity, 0);
            support.SetCoefficient(supportedTime, 1);
            var supportedEBikes = Math.Min(slot.RequiredDrivers - 1, fleet.SimultaneousCapacity(
                drivers.Where(employee => employee.DriverProfile!.DriverType == DriverType.EBike), hour, boundaries));
            var bikeSupport = solver.MakeConstraint(double.NegativeInfinity, 0);
            bikeSupport.SetCoefficient(supportedTime, -Math.Max(0, supportedEBikes));
            var companyBikeSupport = solver.MakeConstraint(double.NegativeInfinity, 0);
            companyBikeSupport.SetCoefficient(supportedTime, -fleet.Remaining(DriverType.EBike, hour, boundaries));
            var vehicles = CompanyVehicleFleet.Types.ToDictionary(type => type,
                type => solver.MakeConstraint(0, fleet.Remaining(type, hour, boundaries)));
            foreach (var employee in drivers)
            {
                var allocation = solver.MakeNumVar(0, 1, $"h{employeeHours[employee.Id].Count}_{employee.Id:N}");
                employeeHours[employee.Id].Add(allocation);
                weekly[employee.Id].SetCoefficient(allocation, 1);
                if (!daily.TryGetValue((employee.Id, slot.Date), out var dailyLimit))
                    daily[(employee.Id, slot.Date)] = dailyLimit = solver.MakeConstraint(0, 10);
                dailyLimit.SetCoefficient(allocation, 1);
                demand.SetCoefficient(allocation, 1);
                total.SetCoefficient(allocation, 1);
                // An e-bike can use only the part of this hour with car/moped support.
                // Enforce both individual time and pooled vehicle time: the presence of
                // another e-biker must not inflate either driver's support allowance.
                if (employee.DriverProfile!.DriverType == DriverType.EBike)
                {
                    var individualSupport = solver.MakeConstraint(double.NegativeInfinity, 0);
                    individualSupport.SetCoefficient(allocation, 1);
                    individualSupport.SetCoefficient(supportedTime, -1);
                    bikeSupport.SetCoefficient(allocation, 1);
                    if (!employee.DriverProfile.IsOwn) companyBikeSupport.SetCoefficient(allocation, 1);
                }
                else support.SetCoefficient(allocation, -1);
                if (CompanyVehicleFleet.RequiredBy(employee) is { } type)
                    vehicles[type].SetCoefficient(allocation, 1);
                solver.Objective().SetCoefficient(allocation, 1);
            }
        }
    }

    public double MaximumHours()
    {
        solver.Objective().SetMaximization();
        Solve();
        return Math.Max(0, solver.Objective().Value());
    }

    public Dictionary<Guid, double> AllocateFairly(IReadOnlyDictionary<Guid, double> scores,
        IReadOnlyDictionary<Guid, double> capacities, double maximum)
    {
        if (maximum < 0.000_001) return employeeHours.Keys.ToDictionary(id => id, _ => 0d);
        total.SetBounds(maximum, maximum);
        solver.Objective().Clear();
        var level = solver.MakeNumVar(0, double.PositiveInfinity, "fairLevel");
        var pending = new Dictionary<Guid, Constraint>();
        foreach (var (employeeId, hours) in employeeHours.Where(pair => scores[pair.Key] > 0 && capacities[pair.Key] > 0))
        {
            var share = solver.MakeConstraint(0, double.PositiveInfinity);
            foreach (var hour in hours) share.SetCoefficient(hour, 1);
            share.SetCoefficient(level, -scores[employeeId]);
            pending[employeeId] = share;
        }
        solver.Objective().SetCoefficient(level, 1);
        solver.Objective().SetMaximization();
        // Weighted max-min filling: freeze only drivers whose share cannot grow at
        // this level, then redistribute among the others. Positive duals identify
        // shared hourly bottlenecks (not just each driver's individual weekly cap).
        while (pending.Count > 0)
        {
            Solve();
            var capped = pending.Where(pair => Math.Abs(pair.Value.DualValue()) > 0.000_000_1 ||
                capacities[pair.Key] <= level.SolutionValue() * scores[pair.Key] + 0.000_000_1)
                .Select(pair => pair.Key).ToArray();
            if (capped.Length == 0)
                throw new RosterInputException("The hourly fair allocation could not identify its limiting capacity.");
            var frozenHours = capped.ToDictionary(id => id,
                id => employeeHours[id].Sum(hour => hour.SolutionValue()));
            foreach (var employeeId in capped)
            {
                var share = pending[employeeId];
                var allocated = frozenHours[employeeId];
                share.SetCoefficient(level, 0);
                share.SetBounds(allocated, allocated);
                pending.Remove(employeeId);
            }
        }
        solver.Objective().Clear();
        Solve();
        return employeeHours.ToDictionary(pair => pair.Key,
            pair => Math.Max(0, pair.Value.Sum(hour => hour.SolutionValue())));
    }

    private void Solve()
    {
        if (solver.Solve() != Solver.ResultStatus.OPTIMAL)
            throw new RosterInputException("Approximate hours could not be calculated within the current hourly limits.");
    }

    public void Dispose() => solver.Dispose();
}
