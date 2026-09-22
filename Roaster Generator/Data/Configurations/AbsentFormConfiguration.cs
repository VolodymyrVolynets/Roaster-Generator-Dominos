using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class AbsentFormConfiguration : IEntityTypeConfiguration<AbsentForm>
{
    public void Configure(EntityTypeBuilder<AbsentForm> builder)
    {
        builder.ToTable("absent_forms");
        builder.HasKey(form => form.Id);
        builder.Property(form => form.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(form => form.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(form => form.DriverFullName).HasColumnName("driver_full_name").HasMaxLength(201).IsRequired();
        builder.Property(form => form.PayrollNumber).HasColumnName("payroll_number").HasMaxLength(64);
        // No shift foreign key: saving/regenerating a roster replaces its shift rows.
        builder.Property(form => form.SavedRosterShiftId).HasColumnName("saved_roster_shift_id");
        builder.Property(form => form.ShiftDate).HasColumnName("shift_date").HasColumnType("date").IsRequired();
        builder.Property(form => form.ShiftStartTime).HasColumnName("shift_start_time").HasColumnType("time without time zone").IsRequired();
        builder.Property(form => form.ShiftFinishTime).HasColumnName("shift_finish_time").HasColumnType("time without time zone").IsRequired();
        builder.Property(form => form.NotificationDate).HasColumnName("notification_date").HasColumnType("date").IsRequired();
        builder.Property(form => form.NotificationTime).HasColumnName("notification_time").HasColumnType("time without time zone").IsRequired();
        builder.Property(form => form.NotificationMethod).HasColumnName("notification_method").HasMaxLength(100).IsRequired();
        builder.Property(form => form.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(2000).IsRequired();
        builder.Property(form => form.SubmittedAtUtc).HasColumnName("submitted_at_utc").IsRequired();
        builder.HasOne(form => form.Employee).WithMany().HasForeignKey(form => form.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(form => new { form.EmployeeId, form.NotificationDate, form.NotificationTime });
    }
}
