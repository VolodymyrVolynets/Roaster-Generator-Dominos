using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed record CompanyVehicleFleet(int Cars = 0, int Mopeds = 0, int EBikes = 0)
{
    public static readonly DriverType[] Types = [DriverType.Car, DriverType.Moped, DriverType.EBike];

    public static CompanyVehicleFleet From(RosterSolverOptions options) =>
        new(options.CompanyCars, options.CompanyMopeds, options.CompanyEBikes);

    public int Count(DriverType type) => type switch
    {
        DriverType.Car => Cars,
        DriverType.Moped => Mopeds,
        DriverType.EBike => EBikes,
        _ => 0
    };

    public static DriverType? RequiredBy(Employee employee) =>
        employee.DriverProfile is { IsOwn: false } profile ? profile.DriverType : null;

    public int Remaining(DriverType type, DateTime hour, IReadOnlyList<RosterSolverBoundaryShift> boundaries) =>
        Math.Max(0, Count(type) - boundaries.Count(shift => shift.CompanyVehicleType == type &&
            shift.Start < hour.AddHours(1) && shift.Finish > hour));

    // An employee is useful only if their vehicle and the required support can be present.
    public Employee[] UsableDrivers(IEnumerable<Employee> available, int demand, DateTime hour,
        IReadOnlyList<RosterSolverBoundaryShift> boundaries)
    {
        var drivers = available.Where(employee => employee.DriverProfile is not null &&
            (RequiredBy(employee) is not { } type || Remaining(type, hour, boundaries) > 0)).ToArray();
        var hasSupport = drivers.Any(employee => employee.DriverProfile!.DriverType is DriverType.Car or DriverType.Moped);
        return drivers.Where(employee => employee.DriverProfile!.DriverType != DriverType.EBike || demand > 1 && hasSupport).ToArray();
    }

    public int SimultaneousCapacity(IEnumerable<Employee> drivers, DateTime hour,
        IReadOnlyList<RosterSolverBoundaryShift> boundaries)
    {
        var people = drivers.ToArray();
        return people.Count(employee => RequiredBy(employee) is null) + Types.Sum(type =>
            Math.Min(Remaining(type, hour, boundaries), people.Count(employee => RequiredBy(employee) == type)));
    }
}
