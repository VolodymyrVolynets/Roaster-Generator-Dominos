using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class CompanyVehicleSettingsTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    public void FleetCountsMustBeNonnegativeWithinTheLimit(int? count, bool valid)
    {
        var request = new RosterSettingsRequest { CompanyCars = count, CompanyMopeds = count, CompanyEBikes = count };
        var result = new RosterSettingsRequestValidator().Validate(request);
        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public async Task FleetCountsDefaultToZeroPersistAndSurviveOlderClients()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new RosterSettingsService(db);
        var initial = await service.GetAsync(default);
        Assert.Equal(0, initial.CompanyCars);
        Assert.Equal(0, initial.CompanyMopeds);
        Assert.Equal(0, initial.CompanyEBikes);

        await service.SaveAsync(new RosterSettingsRequest { CompanyCars = 2, CompanyMopeds = 3, CompanyEBikes = 1 }, default);
        db.ChangeTracker.Clear();
        var legacy = await service.SaveAsync(new RosterSettingsRequest(), default);
        Assert.Equal(2, legacy.CompanyCars);
        Assert.Equal(3, legacy.CompanyMopeds);
        Assert.Equal(1, legacy.CompanyEBikes);
        var options = RosterSettingsService.ToOptions(legacy);
        Assert.Equal(2, options.CompanyCars);
        Assert.Equal(3, options.CompanyMopeds);
        Assert.Equal(1, options.CompanyEBikes);

        await service.SaveAsync(new RosterSettingsRequest { CompanyEBikes = 0 }, default);
        db.ChangeTracker.Clear();
        var updated = await service.GetAsync(default);
        Assert.Equal(2, updated.CompanyCars);
        Assert.Equal(0, updated.CompanyEBikes);
    }
}
