using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    public void Configure(EntityTypeBuilder<StoreSettings> builder)
    {
        builder.ToTable("store_settings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(settings => settings.StoreId)
            .HasColumnName("store_id")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(settings => settings.StoreName)
            .HasColumnName("store_name")
            .HasMaxLength(200)
            .IsRequired();
    }
}
