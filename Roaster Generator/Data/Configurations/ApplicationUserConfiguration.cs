using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("uuid");

        builder.HasIndex(user => user.EmployeeId)
            .IsUnique()
            .HasFilter("employee_id IS NOT NULL");
    }
}
