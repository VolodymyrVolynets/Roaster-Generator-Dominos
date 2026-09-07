using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class DemandRowConfiguration : IEntityTypeConfiguration<DemandRow>
{
    public void Configure(EntityTypeBuilder<DemandRow> builder)
    {
        builder.ToTable("demand_rows");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(row => row.DemandPlanId)
            .HasColumnName("demand_plan_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(row => row.Hour)
            .HasColumnName("hour")
            .IsRequired();

        builder.HasOne(row => row.DemandPlan)
            .WithMany(plan => plan.Rows)
            .HasForeignKey(row => row.DemandPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(row => new { row.DemandPlanId, row.Hour })
            .IsUnique();
    }
}
