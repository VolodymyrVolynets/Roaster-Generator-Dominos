using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class RosterShiftConfiguration : IEntityTypeConfiguration<RosterShift>
{
    public void Configure(EntityTypeBuilder<RosterShift> builder)
    {
        builder.ToTable("roster_shifts");

        builder.HasKey(shift => shift.Id);

        builder.Property(shift => shift.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(shift => shift.RosterPlanId)
            .HasColumnName("roster_plan_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(shift => shift.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(shift => shift.Date)
            .HasColumnName("date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(shift => shift.StartTime)
            .HasColumnName("start_time")
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.Property(shift => shift.FinishTime)
            .HasColumnName("finish_time")
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.HasOne(shift => shift.RosterPlan)
            .WithMany(plan => plan.Shifts)
            .HasForeignKey(shift => shift.RosterPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(shift => shift.Employee)
            .WithMany(employee => employee.RosterShifts)
            .HasForeignKey(shift => shift.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(shift => new
        {
            shift.RosterPlanId,
            shift.EmployeeId,
            shift.Date,
            shift.StartTime,
            shift.FinishTime
        }).IsUnique();
    }
}
