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

    public DbSet<SickLeaveRequest> SickLeaveRequests => Set<SickLeaveRequest>();

    public DbSet<AbsentForm> AbsentForms => Set<AbsentForm>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<DemandPlan> DemandPlans => Set<DemandPlan>();

    public DbSet<DemandColumn> DemandColumns => Set<DemandColumn>();

    public DbSet<DemandRow> DemandRows => Set<DemandRow>();

    public DbSet<DemandValue> DemandValues => Set<DemandValue>();

    public DbSet<RosterPlan> RosterPlans => Set<RosterPlan>();

    public DbSet<RosterShift> RosterShifts => Set<RosterShift>();

    public DbSet<RosterGenerationSettings> RosterGenerationSettings => Set<RosterGenerationSettings>();

    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentRequirement> DocumentRequirements => Set<DocumentRequirement>();

    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    public DbSet<DocumentDraft> DocumentDrafts => Set<DocumentDraft>();

    public DbSet<EmployeeDocumentFile> EmployeeDocumentFiles => Set<EmployeeDocumentFile>();

    public DbSet<DocumentAuditEvent> DocumentAuditEvents => Set<DocumentAuditEvent>();

    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    public DbSet<DocumentManagerSignature> DocumentManagerSignatures => Set<DocumentManagerSignature>();

    public DbSet<EmployeeVehicle> EmployeeVehicles => Set<EmployeeVehicle>();

    public DbSet<VehicleChange> VehicleChanges => Set<VehicleChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
