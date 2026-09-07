using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class RosterGenerationSettingsConfiguration : IEntityTypeConfiguration<RosterGenerationSettings>
{
    public void Configure(EntityTypeBuilder<RosterGenerationSettings> builder)
    {
        builder.ToTable("roster_generation_settings");

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(settings => settings.TargetHoursWeight)
            .HasColumnName("target_hours_weight")
            .HasDefaultValue(100)
            .IsRequired();

        builder.Property(settings => settings.LongShiftBonus)
            .HasColumnName("long_shift_bonus")
            .HasDefaultValue(25)
            .IsRequired();

        builder.Property(settings => settings.ShortShiftPenalty)
            .HasColumnName("short_shift_penalty")
            .HasDefaultValue(10)
            .IsRequired();

        builder.Property(settings => settings.LateFinishPenalty)
            .HasColumnName("late_finish_penalty")
            .HasDefaultValue(2)
            .IsRequired();

        builder.Property(settings => settings.EarlyStartPenalty)
            .HasColumnName("early_start_penalty")
            .HasDefaultValue(1)
            .IsRequired();

        builder.HasData(new RosterGenerationSettings
        {
            Id = RosterGenerationSettings.SingletonId,
            TargetHoursWeight = 100,
            LongShiftBonus = 25,
            ShortShiftPenalty = 10,
            LateFinishPenalty = 2,
            EarlyStartPenalty = 1
        });
    }
}
