using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class DemandStaffingTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2.7, 1)]
    [InlineData(2.71, 2)]
    [InlineData(5.4, 2)]
    [InlineData(5.41, 3)]
    public void DriverDemandRoundsUpEveryFractionalEmployee(double deliveries, int expected)
    {
        Assert.Equal(expected, DemandStaffing.Drivers((decimal)deliveries, 2.7m));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 1)]
    [InlineData(20.01, 2)]
    [InlineData(40, 2)]
    public void InsideDemandIncludesManagerAndRoundsUp(double pizzas, int expected)
    {
        Assert.Equal(expected, DemandStaffing.Inside((decimal)pizzas, 20m, true));
    }

    [Fact]
    public void MissingPizzaCountAndClosedHoursDoNotInventInsideDemand()
    {
        Assert.Null(DemandStaffing.Inside(null, 20m, true));
        Assert.Null(DemandStaffing.Inside(40m, 20m, false));
        Assert.Null(DemandStaffing.Drivers(null, 2.7m));
    }

    [Fact]
    public void ImportRetainsPizzaAndDeliveryPairsForAllSevenDays()
    {
        var parsed = DemandService.ParseText("Hour,Pizzas,Deliveries\n12,20,2.7,21,3,22,4,23,5,24,6,25,7,26,8");

        Assert.Equal(7, parsed.Columns.Count);
        Assert.Equal(7, Assert.Single(parsed.Rows).Values.Count);
        for (var position = 0; position < 7; position++)
        {
            var cell = parsed.Rows[0].Values[position];
            Assert.Equal(20m + position, cell.Pizzas);
            Assert.Equal(position == 0 ? 2.7m : position + 2m, cell.Deliveries);
        }
    }

    [Fact]
    public void EntireBlankPizzaColumnDoesNotShiftFollowingDayPairs()
    {
        var parsed = DemandService.ParseText("12,,2,20,3,20,4,20,5,20,6,20,7,20,8");

        Assert.Null(parsed.Rows[0].Values[0].Pizzas);
        Assert.Equal(2m, parsed.Rows[0].Values[0].Deliveries);
        Assert.Equal(20m, parsed.Rows[0].Values[1].Pizzas);
        Assert.Equal(3m, parsed.Rows[0].Values[1].Deliveries);
        Assert.Equal(8m, parsed.Rows[0].Values[6].Deliveries);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("unknown")]
    [InlineData("1000001")]
    public void InvalidImportedPizzaCountIdentifiesDayAndHour(string count)
    {
        var error = Assert.Throws<DemandValidationException>(() =>
            DemandService.ParseText($"12,{count},2,20,3,20,4,20,5,20,6,20,7,20,8"));

        Assert.Contains("Monday 12:00 pizzas", error.Message);
    }

    [Fact]
    public void RecalculationUsesSavedProductivityAndOnlyAddsManagerDuringOpeningHours()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var service = new DemandService(db, Options.Create(new ShopHoursOptions()));
        var content = string.Join('\n', new[] { 11, 12, 0, 1 }.Select(hour =>
            $"{hour},40,8,40,8,40,8,40,8,40,8,40,8,40,8"));
        var parsed = DemandService.ParseText(content);

        var recalculated = service.Recalculate(parsed, Monday, 4m, 10m);
        foreach (var row in recalculated.Rows)
        {
            Assert.All(row.Values, value => Assert.Equal(2, value.Demand));
            Assert.All(row.Values, value => Assert.Equal(row.Hour is 12 or 0 ? (int?)4 : null, value.InsideDemand));
        }
    }

    [Fact]
    public void ManualInsideDemandCannotBypassMissingPizzaCountsOrMinimumManager()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var service = new DemandService(db, Options.Create(new ShopHoursOptions()));
        var parsed = DemandService.ParseText("12,,2,0,3,20,4,20,5,20,6,20,7,20,8");
        var originalRow = parsed.Rows[0];
        parsed.Rows[0] = originalRow with
        {
            Values = originalRow.Values.Select(value => value with { InsideDemand = 0 }).ToList()
        };

        var normalized = service.NormalizeAndValidateDemand(parsed, Monday);
        var noon = normalized.Rows.Single(row => row.Hour == 12);
        Assert.Null(noon.Values[0].InsideDemand);
        Assert.Equal(1, noon.Values[1].InsideDemand);
    }

    [Fact]
    public void StaffingTotalsIncludeBothTeamsAndSalesButExcludeClosedHoursAndLabour()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var service = new DemandService(db, Options.Create(new ShopHoursOptions()));
        var plan = new DemandPlan { WeekStart = Monday };
        for (var position = 0; position < 7; position++)
        {
            plan.Columns.Add(new DemandColumn
            {
                Id = Guid.NewGuid(), Position = position, TargetSales = position is 0 or 6 ? 100m : 0m
            });
        }
        foreach (var hour in new[] { 11, 12 })
        {
            var row = new DemandRow { Hour = hour };
            foreach (var column in plan.Columns)
                row.Values.Add(new DemandValue
                {
                    DemandColumnId = column.Id,
                    Demand = column.Position is 0 or 6 ? 2 : 0,
                    InsideDemand = column.Position is 0 or 6 ? 3 : 0,
                    Pizzas = 50m
                });
            plan.Rows.Add(row);
        }

        var response = service.ToResponse(plan);
        Assert.Equal(4, response.WeeklyDriverHours);
        Assert.Equal(6, response.WeeklyInsideHours);
        Assert.Equal(200m, response.WeeklyTargetSales);
        var sunday = response.DailyStaffing.Single(day => day.Position == 6);
        Assert.Equal(2, sunday.RequiredDriverHours);
        Assert.Equal(3, sunday.RequiredInsideHours);
        Assert.Equal(100m, sunday.TargetSales);
        Assert.All(response.Rows.Single(row => row.Hour == 11).Values, value => Assert.False(value.IsOpen));
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("hourlyRate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("labour", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void SettingsRejectInvalidProductivity(double productivity)
    {
        var request = ValidRequest();
        request.DeliveriesPerDriverHour = (decimal)productivity;
        request.PizzasPerInsideHour = (decimal)productivity;

        var result = new DemandPlanUpdateRequestValidator().Validate(request);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.DeliveriesPerDriverHour));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.PizzasPerInsideHour));
    }

    [Fact]
    public void SettingsAcceptDefaultsButRejectNegativeWorkload()
    {
        Assert.True(new DemandPlanUpdateRequestValidator().Validate(ValidRequest()).IsValid);
        var result = new DemandValueRequestValidator().Validate(new DemandValueRequest
        {
            Position = 0, Deliveries = -1m, Pizzas = -1m, InsideDemand = -1
        });
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void LegacyDemandOnlyEditPreservesWorkloadsAndInsideDemand()
    {
        var edit = JsonSerializer.Deserialize<DemandValueRequest>("{\"position\":0,\"demand\":7}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings(), true);

        Assert.Equal(8m, result.Deliveries);
        Assert.Equal(40m, result.Pizzas);
        Assert.Equal(7, result.Demand);
        Assert.Equal(3, result.InsideDemand);
    }

    [Fact]
    public void ExplicitNullWorkloadsAreClearedAndAreDifferentFromOmittedFields()
    {
        var edit = JsonSerializer.Deserialize<DemandValueRequest>("{\"position\":0,\"deliveries\":null,\"pizzas\":null}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings(), true);

        Assert.Null(result.Deliveries);
        Assert.Null(result.Pizzas);
        Assert.Null(result.Demand);
        Assert.Null(result.InsideDemand);
    }

    [Fact]
    public void RawWorkloadEditCalculatesStaffingWhenClientOmitsStaffCounts()
    {
        var result = DemandService.ApplyValueEdit(PreviousValue(), new DemandValueRequest
        {
            Position = 0, Deliveries = 12.1m, Pizzas = 80.1m
        }, SavedSettings(), true);

        Assert.Equal(4, result.Demand);
        Assert.Equal(3, result.InsideDemand);
    }

    [Fact]
    public void ExplicitManualStaffCountsSurviveRawWorkloadEditsUntilRecalculationRequested()
    {
        var edit = new DemandValueRequest
        {
            Position = 0, Deliveries = 12.1m, Pizzas = 80.1m, Demand = 5, InsideDemand = 4
        };
        var manual = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings(), true);
        Assert.Equal(5, manual.Demand);
        Assert.Equal(4, manual.InsideDemand);

        var recalculated = DemandService.ApplyValueEdit(PreviousValue(), edit,
            SavedSettings() with { Recalculate = true }, true);
        Assert.Equal(4, recalculated.Demand);
        Assert.Equal(3, recalculated.InsideDemand);
    }

    [Fact]
    public void OmittedNewSettingsPreserveSavedSettingsAndManualDemands()
    {
        var plan = new DemandPlan { DeliveriesPerDriverHour = 4m, PizzasPerInsideHour = 40m };
        var oldRequest = JsonSerializer.Deserialize<DemandPlanUpdateRequest>("{\"hourlyRate\":12}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var settings = DemandService.SettingsForUpdate(plan, oldRequest);

        Assert.Equal(4m, settings.DeliveriesPerDriverHour);
        Assert.Equal(40m, settings.PizzasPerInsideHour);
        Assert.False(settings.Recalculate);
    }

    [Fact]
    public void ExplicitDefaultProductivityReplacesCustomSettings()
    {
        var plan = new DemandPlan { DeliveriesPerDriverHour = 4m, PizzasPerInsideHour = 40m };
        var edit = JsonSerializer.Deserialize<DemandPlanUpdateRequest>(
            "{\"deliveriesPerDriverHour\":2.7,\"pizzasPerInsideHour\":20}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var settings = DemandService.SettingsForUpdate(plan, edit);

        Assert.Equal(2.7m, settings.DeliveriesPerDriverHour);
        Assert.Equal(20m, settings.PizzasPerInsideHour);
        Assert.True(settings.Recalculate);
    }

    private static DemandValue PreviousValue() => new() { Deliveries = 8m, Pizzas = 40m, Demand = 4, InsideDemand = 3 };

    private static DemandService.DemandEditSettings SavedSettings() => new(4m, 40m, false);

    private static DemandPlanUpdateRequest ValidRequest() => new()
    {
        Name = "Week", WeekStart = Monday,
        Columns = Enumerable.Range(0, 7).Select(position => new DemandColumnRequest { Position = position }).ToList(),
        Rows = [new DemandRowRequest
        {
            Hour = 12,
            Values = Enumerable.Range(0, 7).Select(position => new DemandValueRequest
            {
                Position = position, Deliveries = 0, Pizzas = 0, Demand = 0, InsideDemand = 1
            }).ToList()
        }]
    };
}
