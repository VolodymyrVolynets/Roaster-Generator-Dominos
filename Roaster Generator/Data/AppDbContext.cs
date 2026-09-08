using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();

    public DbSet<InStoreProfile> InStoreProfiles => Set<InStoreProfile>();

    public DbSet<ManagerProfile> ManagerProfiles => Set<ManagerProfile>();

    public DbSet<HolidayRequest> HolidayRequests => Set<HolidayRequest>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<DemandPlan> DemandPlans => Set<DemandPlan>();

    public DbSet<DemandColumn> DemandColumns => Set<DemandColumn>();

    public DbSet<DemandRow> DemandRows => Set<DemandRow>();

    public DbSet<DemandValue> DemandValues => Set<DemandValue>();

    public DbSet<RosterPlan> RosterPlans => Set<RosterPlan>();

    public DbSet<RosterShift> RosterShifts => Set<RosterShift>();

    public DbSet<RosterGenerationSettings> RosterGenerationSettings => Set<RosterGenerationSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
