using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class DemandPlanConfiguration : IEntityTypeConfiguration<DemandPlan>
{
    public void Configure(EntityTypeBuilder<DemandPlan> builder)
    {
        builder.ToTable("demand_plans");

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(plan => plan.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(plan => plan.WeekStart)
            .HasColumnName("week_start")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(plan => plan.HourlyRate)
            .HasColumnName("hourly_rate")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(plan => plan.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(plan => plan.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(plan => plan.WeekStart)
            .IsUnique();
    }
}
