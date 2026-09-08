using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class InStoreProfileConfiguration : IEntityTypeConfiguration<InStoreProfile>
{
    public void Configure(EntityTypeBuilder<InStoreProfile> builder)
    {
        builder.ToTable("instore_profiles");

        builder.HasKey(profile => profile.EmployeeId);

        builder.Property(profile => profile.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid");

        builder.HasOne(profile => profile.Employee)
            .WithOne(employee => employee.InStoreProfile)
            .HasForeignKey<InStoreProfile>(profile => profile.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
