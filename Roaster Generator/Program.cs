using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddOptions<ShopHoursOptions>()
    .Bind(builder.Configuration.GetSection(ShopHoursOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 5;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.Configure<CookieAuthenticationOptions>(
    IdentityConstants.ApplicationScheme,
    options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<IValidator<EmployeeRequest>, EmployeeRequestValidator>();
builder.Services.AddScoped<IValidator<DemandImportRequest>, DemandImportRequestValidator>();
builder.Services.AddScoped<IValidator<DemandPlanUpdateRequest>, DemandPlanUpdateRequestValidator>();
builder.Services.AddScoped<DemandService>();
builder.Services.AddScoped<WeeklyScheduleService>();
builder.Services.AddScoped<RosterPlanService>();
builder.Services.AddScoped<RosterSettingsService>();
builder.Services.AddScoped<RosterInputService>();
builder.Services.AddSingleton<RosterTimerService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RosterTimerService>());
builder.Services.AddScoped<IValidator<WeeklyScheduleRequest>, WeeklyScheduleRequestValidator>();
builder.Services.AddScoped<IValidator<WeekSelectionRequest>, WeekSelectionRequestValidator>();
builder.Services.AddScoped<IValidator<RosterPlanUpdateRequest>, RosterPlanUpdateRequestValidator>();
builder.Services.AddScoped<IValidator<RosterSettingsRequest>, RosterSettingsRequestValidator>();

var app = builder.Build();

app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}

app.MapControllers();
app.MapHub<Roaster_Generator.Hubs.RosterTimerHub>("/hubs/roster-timer");
app.MapHub<Roaster_Generator.Hubs.RosterTimerHub>("/hubs/roster-generation");

app.Run();
