using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class DemandValueConfiguration : IEntityTypeConfiguration<DemandValue>
{
    public void Configure(EntityTypeBuilder<DemandValue> builder)
    {
        builder.ToTable("demand_values");

        builder.HasKey(value => value.Id);

        builder.Property(value => value.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(value => value.DemandRowId)
            .HasColumnName("demand_row_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.DemandColumnId)
            .HasColumnName("demand_column_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.Deliveries)
            .HasColumnName("deliveries")
            .HasColumnType("numeric(10,2)");

        builder.Property(value => value.Demand)
            .HasColumnName("demand");

        builder.HasOne(value => value.Row)
            .WithMany(row => row.Values)
            .HasForeignKey(value => value.DemandRowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.Column)
            .WithMany(column => column.Values)
            .HasForeignKey(value => value.DemandColumnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(value => new { value.DemandRowId, value.DemandColumnId })
            .IsUnique();
    }
}
