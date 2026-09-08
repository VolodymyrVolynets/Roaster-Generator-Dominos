using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class DemandColumnConfiguration : IEntityTypeConfiguration<DemandColumn>
{
    public void Configure(EntityTypeBuilder<DemandColumn> builder)
    {
        builder.ToTable("demand_columns");

        builder.HasKey(column => column.Id);

        builder.Property(column => column.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(column => column.DemandPlanId)
            .HasColumnName("demand_plan_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(column => column.Position)
            .HasColumnName("position")
            .IsRequired();

        builder.Property(column => column.Label)
            .HasColumnName("label")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(column => column.TargetSales)
            .HasColumnName("target_sales")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.HasOne(column => column.DemandPlan)
            .WithMany(plan => plan.Columns)
            .HasForeignKey(column => column.DemandPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(column => new { column.DemandPlanId, column.Position })
            .IsUnique();
    }
}
