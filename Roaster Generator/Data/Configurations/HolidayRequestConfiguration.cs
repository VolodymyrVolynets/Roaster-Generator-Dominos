using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Data.Configurations;

public sealed class HolidayRequestConfiguration : IEntityTypeConfiguration<HolidayRequest>
{
    public void Configure(EntityTypeBuilder<HolidayRequest> builder)
    {
        builder.ToTable("holiday_requests");

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(request => request.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(request => request.Hours)
            .HasColumnName("hours")
            .IsRequired();

        builder.Property(request => request.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(HolidayStatus.Requested)
            .IsRequired();

        builder.Property(request => request.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(request => request.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(request => request.UsedAtUtc)
            .HasColumnName("used_at_utc");

        builder.HasIndex(request => request.EmployeeId)
            .HasDatabaseName("IX_holiday_requests_employee_requested")
            .IsUnique()
            .HasFilter("status = 'Requested'");

        builder.HasIndex(request => new { request.Status, request.UpdatedAtUtc });

        builder.HasOne(request => request.Employee)
            .WithMany()
            .HasForeignKey(request => request.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
