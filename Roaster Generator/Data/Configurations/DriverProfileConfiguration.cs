using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Data.Configurations;

public sealed class DriverProfileConfiguration : IEntityTypeConfiguration<DriverProfile>
{
    public void Configure(EntityTypeBuilder<DriverProfile> builder)
    {
        builder.ToTable("driver_profiles");

        builder.HasKey(profile => profile.EmployeeId);

        builder.Property(profile => profile.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid");

        builder.Property(profile => profile.TargetHours)
            .HasColumnName("target_hours")
            .HasDefaultValue(20)
            .IsRequired();

        builder.Property(profile => profile.CanWorkAlone)
            .HasColumnName("can_work_alone")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(profile => profile.DriverType)
            .HasColumnName("driver_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(DriverType.Car)
            .IsRequired();

        builder.HasOne(profile => profile.Employee)
            .WithOne(employee => employee.DriverProfile)
            .HasForeignKey<DriverProfile>(profile => profile.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
