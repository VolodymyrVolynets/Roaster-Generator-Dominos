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

        builder.Property(settings => settings.DailyOptionPoolSize)
            .HasColumnName("daily_option_pool_size")
            .HasDefaultValue(256)
            .IsRequired();

        builder.Property(settings => settings.DailyOptionSearchNodeLimit)
            .HasColumnName("daily_option_search_node_limit")
            .HasDefaultValue(100_000)
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
            MinimumRestHours = 8,
            PreferredRestHours = 12,
            LatestShiftStartHour = 20,
            MaxSolveSeconds = 20,
            DailyOptionPoolSize = 256,
            DailyOptionSearchNodeLimit = 100_000,
            PopulationSize = 24,
            GenerationCount = 150,
            MutationRate = 0.03m,
            EliteCount = 2,
            TournamentSize = 2,
            ExactSearchNodeLimit = 500_000
        });

        builder.Property(settings => settings.MinimumRestHours)
            .HasColumnName("minimum_rest_hours").HasDefaultValue(8).IsRequired();
        builder.Property(settings => settings.PreferredRestHours)
            .HasColumnName("preferred_rest_hours").HasDefaultValue(12).IsRequired();
        builder.Property(settings => settings.LatestShiftStartHour)
            .HasColumnName("latest_shift_start_hour").HasDefaultValue(20).IsRequired();
        builder.Property(settings => settings.MaxSolveSeconds)
            .HasColumnName("max_solve_seconds").HasDefaultValue(20).IsRequired();
        builder.Property(settings => settings.HistoryFairnessWeight)
            .HasColumnName("history_fairness_weight").HasDefaultValue(100).IsRequired();
        builder.Property(settings => settings.HistoryShiftLengthWeight)
            .HasColumnName("history_shift_length_weight").HasDefaultValue(100).IsRequired();
        builder.Property(settings => settings.FairnessSpreadWeight)
            .HasColumnName("fairness_spread_weight").HasDefaultValue(1000).IsRequired();
    }
}
