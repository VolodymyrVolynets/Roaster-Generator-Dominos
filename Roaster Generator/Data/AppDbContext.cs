using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<DemandPlan> DemandPlans => Set<DemandPlan>();

    public DbSet<DemandColumn> DemandColumns => Set<DemandColumn>();

    public DbSet<DemandRow> DemandRows => Set<DemandRow>();

    public DbSet<DemandValue> DemandValues => Set<DemandValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
