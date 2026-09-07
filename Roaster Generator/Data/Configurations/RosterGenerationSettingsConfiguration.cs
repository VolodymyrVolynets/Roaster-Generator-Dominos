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

        builder.Property(settings => settings.DailyShiftCountPenalty)
            .HasColumnName("daily_shift_count_penalty")
            .HasDefaultValue(25)
            .IsRequired();

        builder.Property(settings => settings.ShortBreakPenalty)
            .HasColumnName("short_break_penalty")
            .HasDefaultValue(100)
            .IsRequired();

        builder.Property(settings => settings.PopulationSize)
            .HasColumnName("population_size")
            .HasDefaultValue(24)
            .IsRequired();

        builder.Property(settings => settings.GenerationCount)
            .HasColumnName("generation_count")
            .HasDefaultValue(150)
            .IsRequired();

        builder.Property(settings => settings.MutationRate)
            .HasColumnName("mutation_rate")
            .HasColumnType("numeric(5,4)")
            .HasDefaultValue(0.03m)
            .IsRequired();

        builder.Property(settings => settings.EliteCount)
            .HasColumnName("elite_count")
            .HasDefaultValue(2)
            .IsRequired();

        builder.Property(settings => settings.TournamentSize)
            .HasColumnName("tournament_size")
            .HasDefaultValue(2)
            .IsRequired();

        builder.Property(settings => settings.ExactSearchNodeLimit)
            .HasColumnName("exact_search_node_limit")
            .HasDefaultValue(500_000)
            .IsRequired();

        builder.HasData(new RosterGenerationSettings
        {
            Id = RosterGenerationSettings.SingletonId,
            TargetHoursWeight = 100,
            LongShiftBonus = 25,
            ShortShiftPenalty = 10,
            DailyShiftCountPenalty = 25,
            ShortBreakPenalty = 100,
            PopulationSize = 24,
            GenerationCount = 150,
            MutationRate = 0.03m,
            EliteCount = 2,
            TournamentSize = 2,
            ExactSearchNodeLimit = 500_000
        });
    }
}
