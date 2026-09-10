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
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

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

    [Fact]
    public void MissingDeliveryCountDoesNotInventDriverDemand()
    {
        Assert.Null(DemandStaffing.Drivers(null, 2.7m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("-1")]
    [InlineData("1000001")]
    public void LegacyImportIgnoresPizzaCellsAndPreservesAllSevenDeliveryPositions(string ignoredPizza)
    {
        var pairs = Enumerable.Range(0, 7).Select(position => $"{ignoredPizza},{position + 2}");
        var parsed = DemandService.ParseText($"Hour,Pizzas,Deliveries\n12,{string.Join(',', pairs)}");

        Assert.Equal(7, parsed.Columns.Count);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(7, row.Values.Count);
        for (var position = 0; position < 7; position++)
        {
            Assert.Equal(position, row.Values[position].Position);
            Assert.Equal(position + 2m, row.Values[position].Deliveries);
        }
    }

    [Fact]
    public void BlankDeliveryColumnDoesNotShiftFollowingDays()
    {
        var parsed = DemandService.ParseText("12,text,,text,3,,4,20,5,20,6,20,7,20,8");

        Assert.Null(parsed.Rows[0].Values[0].Deliveries);
        Assert.Null(parsed.Rows[0].Values[0].Demand);
        Assert.Equal(3m, parsed.Rows[0].Values[1].Deliveries);
        Assert.Equal(8m, parsed.Rows[0].Values[6].Deliveries);
    }

    [Fact]
    public void TrailingBlankDeliveryIsPreservedInTabSeparatedImports()
    {
        var parsed = DemandService.ParseText("12\ttext\t2\ttext\t3\ttext\t4\ttext\t5\ttext\t6\ttext\t7\ttext\t");

        Assert.Equal(7, parsed.Columns.Count);
        Assert.Equal(7m, parsed.Rows[0].Values[5].Deliveries);
        Assert.Null(parsed.Rows[0].Values[6].Deliveries);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("unknown")]
    [InlineData("1000001")]
    public void InvalidImportedDeliveriesIdentifyDayAndHour(string count)
    {
        var error = Assert.Throws<DemandValidationException>(() =>
            DemandService.ParseText($"12,text,{count},20,3,20,4,20,5,20,6,20,7,20,8"));

        Assert.Contains("Monday 12:00 deliveries", error.Message);
    }

    [Fact]
    public void RecalculationUsesSavedDriverProductivity()
    {
        var content = string.Join('\n', new[] { 11, 12, 0, 1 }.Select(hour =>
            $"{hour},ignored,8,ignored,8,ignored,8,ignored,8,ignored,8,ignored,8,ignored,8"));
        var parsed = DemandService.ParseText(content);

        var recalculated = DemandService.Recalculate(parsed, 4m);

        Assert.All(recalculated.Rows, row => Assert.All(row.Values, value => Assert.Equal(2, value.Demand)));
    }

    [Fact]
    public void PizzaOnlyLaterRowsDoNotPreventDeliverySpillUntilClosingTime()
    {
        using var db = NewDb();
        var service = Service(db);
        var content = "23,text,8,text,8,text,8,text,8,text,8,text,8,text,8\n" +
            "0,text,,text,,text,,text,,text,,text,,text,\n" +
            "1,text,,text,,text,,text,,text,,text,,text,";

        var normalized = service.NormalizeAndValidateDemand(DemandService.ParseText(content), Monday);

        Assert.All(normalized.Rows.Single(row => row.Hour == 0).Values, value =>
        {
            Assert.Equal(8m, value.Deliveries);
            Assert.Equal(3, value.Demand);
        });
        Assert.All(normalized.Rows.Single(row => row.Hour == 1).Values, value =>
        {
            Assert.Null(value.Deliveries);
            Assert.Null(value.Demand);
        });
    }

    [Fact]
    public void PizzaOnlyImportDoesNotInventAnyDeliveryDemandOrSpillRows()
    {
        using var db = NewDb();
        var parsed = DemandService.ParseText("12,20,,20,,20,,20,,20,,20,,20,");

        var normalized = Service(db).NormalizeAndValidateDemand(parsed, Monday);

        Assert.All(Assert.Single(normalized.Rows).Values, value => Assert.Null(value.Demand));
    }

    [Fact]
    public void StaffingTotalsShowOnlyDriversAndSalesAndExcludeClosedHours()
    {
        using var db = NewDb();
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

        var response = Service(db).ToResponse(plan);
        Assert.Equal(4, response.WeeklyDriverHours);
        Assert.Equal(200m, response.WeeklyTargetSales);
        var sunday = response.DailyStaffing.Single(day => day.Position == 6);
        Assert.Equal(2, sunday.RequiredDriverHours);
        Assert.Equal(100m, sunday.TargetSales);
        Assert.All(response.Rows.Single(row => row.Hour == 11).Values, value => Assert.False(value.IsOpen));
        var json = JsonSerializer.Serialize(response, WebJson);
        Assert.DoesNotContain("hourlyRate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("labour", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pizza", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inside", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void SettingsRejectInvalidProductivity(double productivity)
    {
        var request = ValidRequest();
        request.DeliveriesPerDriverHour = (decimal)productivity;
        var result = new DemandPlanUpdateRequestValidator().Validate(request);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.DeliveriesPerDriverHour));
    }

    [Fact]
    public void SettingsAcceptDefaultsButRejectNegativeDeliveriesAndDriverDemand()
    {
        Assert.True(new DemandPlanUpdateRequestValidator().Validate(ValidRequest()).IsValid);
        var result = new DemandValueRequestValidator().Validate(new DemandValueRequest
        {
            Position = 0, Deliveries = -1m, Demand = -1
        });
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void RemovedPizzaAndInsideRequestFieldsDoNotBlockDriverEdits()
    {
        var json = JsonSerializer.Serialize(ValidRequest(), WebJson);
        var request = JsonSerializer.Deserialize<DemandPlanUpdateRequest>(
            json.Insert(1, "\"pizzasPerInsideHour\":0,"), WebJson)!;
        request.Rows[0].Values[0] = JsonSerializer.Deserialize<DemandValueRequest>(
            "{\"position\":0,\"deliveries\":8,\"demand\":3,\"pizzas\":\"unknown\",\"insideDemand\":-1}", WebJson)!;

        Assert.True(new DemandPlanUpdateRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void DemandOnlyEditPreservesDeliveries()
    {
        var edit = JsonSerializer.Deserialize<DemandValueRequest>("{\"position\":0,\"demand\":7}", WebJson)!;
        var result = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings());

        Assert.Equal(8m, result.Deliveries);
        Assert.Equal(7, result.Demand);
    }

    [Fact]
    public void ExplicitNullDeliveriesAreClearedAndDifferFromOmittedFields()
    {
        var edit = JsonSerializer.Deserialize<DemandValueRequest>("{\"position\":0,\"deliveries\":null}", WebJson)!;
        var result = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings());

        Assert.Null(result.Deliveries);
        Assert.Null(result.Demand);
    }

    [Fact]
    public void DeliveryEditCalculatesStaffingWhenClientOmitsStaffCount()
    {
        var result = DemandService.ApplyValueEdit(PreviousValue(), new DemandValueRequest
        {
            Position = 0, Deliveries = 12.1m
        }, SavedSettings());

        Assert.Equal(4, result.Demand);
    }

    [Fact]
    public void ExplicitManualStaffCountSurvivesDeliveryEditsUntilRecalculationRequested()
    {
        var edit = new DemandValueRequest { Position = 0, Deliveries = 12.1m, Demand = 5 };
        var manual = DemandService.ApplyValueEdit(PreviousValue(), edit, SavedSettings());
        Assert.Equal(5, manual.Demand);

        var recalculated = DemandService.ApplyValueEdit(PreviousValue(), edit,
            SavedSettings() with { Recalculate = true });
        Assert.Equal(4, recalculated.Demand);
    }

    [Fact]
    public void OmittedDriverProductivityPreservesSavedSettingAndManualDemands()
    {
        var plan = new DemandPlan { DeliveriesPerDriverHour = 4m, PizzasPerInsideHour = 40m };
        var oldRequest = JsonSerializer.Deserialize<DemandPlanUpdateRequest>(
            "{\"hourlyRate\":12,\"pizzasPerInsideHour\":20}", WebJson)!;
        var settings = DemandService.SettingsForUpdate(plan, oldRequest);

        Assert.Equal(4m, settings.DeliveriesPerDriverHour);
        Assert.False(settings.Recalculate);
    }

    [Fact]
    public void ExplicitDefaultProductivityReplacesCustomSetting()
    {
        var plan = new DemandPlan { DeliveriesPerDriverHour = 4m };
        var edit = JsonSerializer.Deserialize<DemandPlanUpdateRequest>("{\"deliveriesPerDriverHour\":2.7}", WebJson)!;
        var settings = DemandService.SettingsForUpdate(plan, edit);

        Assert.Equal(2.7m, settings.DeliveriesPerDriverHour);
        Assert.True(settings.Recalculate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatingOrReimportingDriverDemandPreservesStoredInsideData(bool reimport)
    {
        using var db = NewDb();
        var service = Service(db);
        var imported = await service.ImportTextAsync(new DemandImportRequest
        {
            Name = "Week", WeekStart = Monday,
            Content = "12,ignored,8,ignored,8,ignored,8,ignored,8,ignored,8,ignored,8,ignored,8"
        }, CancellationToken.None);
        var plan = await db.DemandPlans.SingleAsync();
        plan.PizzasPerInsideHour = 0m;
        plan.Columns.First().TargetSales = 100m;
        foreach (var value in plan.Rows.SelectMany(row => row.Values))
        {
            value.Pizzas = 40m;
            value.InsideDemand = 3;
        }
        await db.SaveChangesAsync();

        if (reimport)
        {
            await service.ImportTextAsync(new DemandImportRequest
            {
                Name = "Updated", WeekStart = Monday,
                Content = "12,,4,,4,,4,,4,,4,,4,,4"
            }, CancellationToken.None);
        }
        else
        {
            var request = new DemandPlanUpdateRequest
            {
                Name = "Updated", WeekStart = Monday, DeliveriesPerDriverHour = 4m,
                Columns = imported.Columns.Select(column => new DemandColumnRequest
                {
                    Position = column.Position, TargetSales = 100m
                }).ToList(),
                Rows = imported.Rows.Select(row => new DemandRowRequest
                {
                    Hour = row.Hour,
                    Values = row.Values.Select(value => new DemandValueRequest
                    {
                        Position = value.Position, Deliveries = 4m
                    }).ToList()
                }).ToList()
            };
            await service.UpdateAsync(imported.Id, request, CancellationToken.None);
        }

        db.ChangeTracker.Clear();
        var saved = await db.DemandPlans.Include(item => item.Columns)
            .Include(item => item.Rows).ThenInclude(row => row.Values).SingleAsync();
        Assert.Equal(0m, saved.PizzasPerInsideHour);
        Assert.Contains(saved.Columns, column => column.TargetSales == 100m);
        Assert.All(saved.Rows.SelectMany(row => row.Values), value =>
        {
            Assert.Equal(4m, value.Deliveries);
            Assert.Equal(reimport ? 2 : 1, value.Demand);
            Assert.Equal(40m, value.Pizzas);
            Assert.Equal(3, value.InsideDemand);
        });
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DemandService Service(AppDbContext db) => new(db, Options.Create(new ShopHoursOptions()));

    private static DemandValue PreviousValue() => new() { Deliveries = 8m, Pizzas = 40m, Demand = 4, InsideDemand = 3 };

    private static DemandService.DemandEditSettings SavedSettings() => new(4m, false);

    private static DemandPlanUpdateRequest ValidRequest() => new()
    {
        Name = "Week", WeekStart = Monday,
        Columns = Enumerable.Range(0, 7).Select(position => new DemandColumnRequest { Position = position }).ToList(),
        Rows = [new DemandRowRequest
        {
            Hour = 12,
            Values = Enumerable.Range(0, 7).Select(position => new DemandValueRequest
            {
                Position = position, Deliveries = 0, Demand = 0
            }).ToList()
        }]
    };
}
