using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class ManagerProfileConfiguration : IEntityTypeConfiguration<ManagerProfile>
{
    public void Configure(EntityTypeBuilder<ManagerProfile> builder)
    {
        builder.ToTable("manager_profiles");

        builder.HasKey(profile => profile.EmployeeId);

        builder.Property(profile => profile.TargetHours)
            .HasColumnName("target_hours").HasDefaultValue(20).IsRequired();

        builder.Property(profile => profile.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid");

        builder.HasOne(profile => profile.Employee)
            .WithOne(employee => employee.ManagerProfile)
            .HasForeignKey<ManagerProfile>(profile => profile.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
