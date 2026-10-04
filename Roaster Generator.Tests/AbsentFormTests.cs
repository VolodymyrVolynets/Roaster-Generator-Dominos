using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Absent;
using Roaster_Generator.Contracts.Settings;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class AbsentFormTests
{
    [Fact]
    public async Task DriverCanSubmitMultipleFormsAndOnlySeesOwnFormsAndSavedDriverShifts()
    {
        using var fixture = new Fixture();
        var first = await fixture.Submit();
        var second = await fixture.Submit();
        await fixture.Service.SubmitAsync(fixture.OtherEmployee, fixture.Request(fixture.OtherShift.Id), default);

        var overview = Read<DriverAbsentOverviewResponse>(await fixture.Controller.Get(default));
        Assert.Equal(2, overview.Forms.Count);
        Assert.NotEqual(first.Id, second.Id);
        Assert.All(overview.Forms, form => Assert.Equal(fixture.Employee.Id, form.EmployeeId));
        Assert.Equal(new Guid?[] { fixture.FutureShift.Id, fixture.OwnShift.Id }, overview.Shifts.Select(shift => shift.Id));
        Assert.Equal(4, await fixture.Db.RosterShifts.CountAsync());
        Assert.Equal(3, await fixture.Db.AbsentForms.CountAsync());
    }

    [Fact]
    public async Task IdentityAndSavedShiftArePopulatedByServerWhileNotificationDateIsEditable()
    {
        using var fixture = new Fixture();
        var json = JsonSerializer.Serialize(new
        {
            savedRosterShiftId = fixture.OwnShift.Id,
            employeeId = fixture.OtherEmployee.Id,
            driverFullName = "Forged name", payrollNumber = "Forged payroll",
            notificationDate = "2000-01-01",
            notificationTime = "00:00", notificationMethod = " Phone call ",
            cancellationReason = " Unable to attend "
        });
        var request = JsonSerializer.Deserialize<AbsentFormCreateRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var form = Read<AbsentFormResponse>(await fixture.Controller.Create(request, default));

        Assert.Equal("Jamie Driver", form.DriverFullName);
        Assert.Equal("PAY-001", form.PayrollNumber);
        Assert.Equal(fixture.Employee.Id, form.EmployeeId);
        Assert.Equal(new DateOnly(2000, 1, 1), form.NotificationDate);
        Assert.Equal("00:00", form.NotificationTime);
        Assert.Equal("Phone call", form.NotificationMethod);
        Assert.Equal("Unable to attend", form.CancellationReason);
        Assert.Equal(fixture.OwnShift.Date, form.Shift.Date);
        Assert.Equal(1, form.Shift.FinishDayOffset);
    }

    [Fact]
    public async Task DriverCanEnterShiftManuallyWhenSavedRosterIsMissingOrOutOfDate()
    {
        using var fixture = new Fixture();
        fixture.Db.RosterShifts.RemoveRange(fixture.Db.RosterShifts);
        fixture.Db.RosterPlans.RemoveRange(fixture.Db.RosterPlans);
        await fixture.Db.SaveChangesAsync();
        var request = fixture.Request();
        request.SavedRosterShiftId = null;
        request.ShiftDate = new DateOnly(2026, 9, 25);
        request.ShiftStartTime = "22:00";
        request.ShiftFinishTime = "03:00";
        request.NotificationDate = new DateOnly(2026, 9, 24);
        request.NotificationTime = "19:00";

        var form = Read<AbsentFormResponse>(await fixture.Controller.Create(request, default));

        Assert.Null(form.Shift.Id);
        Assert.Equal(new DateOnly(2026, 9, 25), form.Shift.Date);
        Assert.Equal("22:00", form.Shift.StartTime);
        Assert.Equal("03:00", form.Shift.FinishTime);
        Assert.Equal(1, form.Shift.FinishDayOffset);
        Assert.Equal(new DateOnly(2026, 9, 24), form.NotificationDate);
        Assert.Equal("19:00", form.NotificationTime);
    }

    [Theory]
    [InlineData("other-driver")]
    [InlineData("inside-roster")]
    [InlineData("missing-shift")]
    [InlineData("removed-shift")]
    public async Task CannotSubmitForAnotherDriverInsideRosterOrMissingSavedShift(string scenario)
    {
        using var fixture = new Fixture();
        var id = scenario switch
        {
            "other-driver" => fixture.OtherShift.Id,
            "inside-roster" => fixture.InsideShift.Id,
            "removed-shift" => fixture.OwnShift.Id,
            _ => Guid.NewGuid()
        };
        if (scenario == "removed-shift")
        {
            fixture.Db.RosterShifts.Remove(fixture.OwnShift);
            await fixture.Db.SaveChangesAsync();
        }

        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Create(fixture.Request(id), default));
        Assert.Empty(await fixture.Db.AbsentForms.ToListAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unlinked")]
    [InlineData("missing-user")]
    [InlineData("role-removed")]
    public async Task InvalidDriverStateBlocksBothListingAndSubmitting(string scenario)
    {
        using var fixture = new Fixture();
        if (scenario == "inactive") fixture.Employee.IsActive = false;
        if (scenario == "unlinked")
        {
            fixture.User.EmployeeId = null;
            fixture.User.Employee = null;
        }
        if (scenario == "missing-user") fixture.Db.Users.Remove(fixture.User);
        if (scenario == "role-removed") fixture.Db.UserRoles.RemoveRange(fixture.Db.UserRoles);
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<ForbidResult>(await fixture.Controller.Get(default));
        Assert.IsType<ForbidResult>(await fixture.Controller.Create(fixture.Request(), default));
        Assert.Empty(await fixture.Db.AbsentForms.ToListAsync());
    }

    [Fact]
    public async Task SubmittedDetailsSurviveEmployeeChangesAndRemovalOfSavedRoster()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        fixture.Employee.FirstName = "Updated";
        fixture.Employee.PayrollNumber = "PAY-NEW";
        fixture.Db.RosterShifts.RemoveRange(fixture.Db.RosterShifts);
        fixture.Db.RosterPlans.RemoveRange(fixture.Db.RosterPlans);
        await fixture.Db.SaveChangesAsync();

        var overview = Read<DriverAbsentOverviewResponse>(await fixture.Controller.Get(default));
        Assert.Empty(overview.Shifts);
        var saved = Assert.Single(overview.Forms);
        Assert.Equal("Jamie Driver", saved.DriverFullName);
        Assert.Equal("PAY-001", saved.PayrollNumber);
        Assert.Equal(submitted.Shift.Date, saved.Shift.Date);
        Assert.Equal(submitted.Shift.StartTime, saved.Shift.StartTime);
        Assert.Equal(submitted.Shift.FinishTime, saved.Shift.FinishTime);
        Assert.Equal("Updated Driver", overview.DriverFullName);
    }

    [Fact]
    public async Task AdminReceivesAllFormsGroupedByDriverWithNewestNotificationDatesAndTimesFirst()
    {
        using var fixture = new Fixture();
        var yesterday = await fixture.Submit();
        var earlierToday = await fixture.Submit("09:00", new DateOnly(2026, 9, 23));
        var laterToday = await fixture.Submit("17:00", new DateOnly(2026, 9, 23));
        await fixture.Service.SubmitAsync(fixture.OtherEmployee, fixture.Request(fixture.OtherShift.Id), default);
        fixture.OtherEmployee.IsActive = false;
        await fixture.Db.SaveChangesAsync();

        var groups = Read<IReadOnlyList<DriverAbsentGroupResponse>>(await fixture.AdminController.Get(default));
        Assert.Equal(2, groups.Count);
        Assert.Equal("Alex Driver", groups[0].DriverFullName);
        Assert.Single(groups[0].Forms);
        Assert.Equal(new[] { laterToday.Id, earlierToday.Id, yesterday.Id }, groups[1].Forms.Select(form => form.Id));
    }

    [Fact]
    public async Task ManagementFiltersUseShiftCalendarDatesAndEmployeeName()
    {
        using var fixture = new Fixture();
        var currentWeek = await fixture.Submit();
        var futureWeek = Read<AbsentFormResponse>(await fixture.Controller.Create(
            fixture.Request(fixture.FutureShift.Id), default));
        var otherDriverId = (await fixture.Service.SubmitAsync(
            fixture.OtherEmployee, fixture.Request(fixture.OtherShift.Id), default))!.Id;

        var byStartDate = Read<IReadOnlyList<DriverAbsentGroupResponse>>(
            await fixture.AdminController.Get(new AbsentManagementFilter
            {
                ShiftStartDate = fixture.FutureShift.Date
            }, default));
        Assert.Single(byStartDate);
        Assert.Equal(futureWeek.Id, Assert.Single(byStartDate[0].Forms).Id);

        var byFinishDate = Read<IReadOnlyList<DriverAbsentGroupResponse>>(
            await fixture.AdminController.Get(new AbsentManagementFilter
            {
                ShiftFinishDate = fixture.OwnShift.Date.AddDays(1)
            }, default));
        Assert.Equal(2, byFinishDate.Sum(group => group.Forms.Count));
        Assert.Contains(byFinishDate, group => group.DriverFullName == "Alex Driver");
        Assert.Contains(byFinishDate, group => group.Forms.Any(form => form.Id == currentWeek.Id));

        var byName = Read<IReadOnlyList<DriverAbsentGroupResponse>>(
            await fixture.AdminController.Get(new AbsentManagementFilter
            {
                EmployeeName = "alex"
            }, default));
        var matchingGroup = Assert.Single(byName);
        var matchingForm = matchingGroup.Forms.Single();
        Assert.Equal(otherDriverId, matchingForm.Id);
    }

    [Fact]
    public async Task ManagementFiltersRejectFinishDateBeforeStartDate()
    {
        using var fixture = new Fixture();
        var result = await fixture.AdminController.Get(new AbsentManagementFilter
        {
            ShiftStartDate = new DateOnly(2026, 9, 23),
            ShiftFinishDate = new DateOnly(2026, 9, 22)
        }, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AdminCanEditFormAndChooseAnotherSavedShiftForTheSameDriver()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        var result = Read<AbsentFormResponse>(await fixture.AdminController.Update(submitted.Id, new AbsentFormUpdateRequest
        {
            SavedRosterShiftId = fixture.FutureShift.Id,
            NotificationDate = new DateOnly(2026, 9, 30), NotificationTime = "17:00",
            NotificationMethod = " WhatsApp ", CancellationReason = " Corrected reason "
        }, default));

        Assert.Equal("Jamie Driver", result.DriverFullName);
        Assert.Equal("PAY-001", result.PayrollNumber);
        Assert.Equal(fixture.FutureShift.Id, result.Shift.Id);
        Assert.Equal(fixture.FutureShift.Date, result.Shift.Date);
        Assert.Equal(new DateOnly(2026, 9, 30), result.NotificationDate);
        Assert.Equal("17:00", result.NotificationTime);
        Assert.Equal("WhatsApp", result.NotificationMethod);
        Assert.Equal("Corrected reason", result.CancellationReason);
        Assert.Equal(result.Id, Assert.Single(Read<DriverAbsentOverviewResponse>(
            await fixture.Controller.Get(default)).Forms).Id);
    }

    [Fact]
    public async Task AdminCanEditOtherFieldsWhileKeepingRecordedShiftAfterRosterReplacement()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        fixture.Db.RosterShifts.Remove(fixture.OwnShift);
        await fixture.Db.SaveChangesAsync();

        var result = Read<AbsentFormResponse>(await fixture.AdminController.Update(submitted.Id, new AbsentFormUpdateRequest
        {
            SavedRosterShiftId = null,
            NotificationDate = submitted.NotificationDate, NotificationTime = "08:00",
            NotificationMethod = "In person", CancellationReason = "Updated without changing shift"
        }, default));

        Assert.Equal(submitted.Shift.Id, result.Shift.Id);
        Assert.Equal(submitted.Shift.Date, result.Shift.Date);
        Assert.Equal("08:00", result.NotificationTime);
    }

    [Fact]
    public async Task AdminCanReplaceRecordedShiftWithManualShift()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        var result = Read<AbsentFormResponse>(await fixture.AdminController.Update(submitted.Id,
            new AbsentFormUpdateRequest
            {
                ShiftDate = new DateOnly(2026, 10, 2), ShiftStartTime = "21:00", ShiftFinishTime = "02:00",
                NotificationDate = submitted.NotificationDate, NotificationTime = "18:00",
                NotificationMethod = "Phone", CancellationReason = "Roster had not been updated"
            }, default));

        Assert.Null(result.Shift.Id);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Shift.Date);
        Assert.Equal("21:00", result.Shift.StartTime);
        Assert.Equal("02:00", result.Shift.FinishTime);
    }

    [Theory]
    [InlineData("other-driver")]
    [InlineData("inside-roster")]
    [InlineData("missing-shift")]
    public async Task AdminCannotMoveFormToAnotherDriversInsideOrMissingShift(string scenario)
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        var shiftId = scenario switch
        {
            "other-driver" => fixture.OtherShift.Id,
            "inside-roster" => fixture.InsideShift.Id,
            _ => Guid.NewGuid()
        };
        var result = await fixture.AdminController.Update(submitted.Id, new AbsentFormUpdateRequest
        {
            SavedRosterShiftId = shiftId,
            NotificationDate = submitted.NotificationDate, NotificationTime = "09:00",
            NotificationMethod = "Text", CancellationReason = "Attempted update"
        }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        var stored = await fixture.Db.AbsentForms.SingleAsync();
        Assert.Equal(fixture.OwnShift.Id, stored.SavedRosterShiftId);
        Assert.Equal("12:00", stored.NotificationTime.ToString("HH:mm"));
    }

    [Fact]
    public async Task AdminCanDeleteFormAndMissingMutationsReturnNotFound()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        Assert.IsType<NoContentResult>(await fixture.AdminController.Delete(submitted.Id, default));
        Assert.Empty(await fixture.Db.AbsentForms.ToListAsync());
        Assert.IsType<NotFoundObjectResult>(await fixture.AdminController.Delete(submitted.Id, default));
        Assert.IsType<NotFoundObjectResult>(await fixture.AdminController.Update(submitted.Id, new AbsentFormUpdateRequest
        {
            NotificationDate = submitted.NotificationDate, NotificationTime = "10:00",
            NotificationMethod = "Phone", CancellationReason = "No longer exists"
        }, default));
    }

    [Theory]
    [InlineData("2026-09-21T23:30:00Z", "2026-09-22")]
    [InlineData("2026-12-21T23:30:00Z", "2026-12-21")]
    public async Task OverviewDefaultsNotificationDateToCurrentDublinDate(string utc, string expected)
    {
        using var fixture = new Fixture();
        fixture.Clock.Now = DateTimeOffset.Parse(utc);
        Assert.Equal(DateOnly.Parse(expected), Read<DriverAbsentOverviewResponse>(
            await fixture.Controller.Get(default)).NotificationDate);
    }

    [Fact]
    public async Task MissingPayrollNumberDoesNotPreventSubmission()
    {
        using var fixture = new Fixture();
        fixture.Employee.PayrollNumber = null;
        await fixture.Db.SaveChangesAsync();
        Assert.Null((await fixture.Submit()).PayrollNumber);
    }

    [Fact]
    public async Task ManagementDownloadsPrefilledFillablePdfWithRequestedFileName()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();

        var file = Assert.IsType<FileContentResult>(
            await fixture.AdminController.DownloadPdf(submitted.Id, default));

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("Jamie Driver 2026-09-21 absent.pdf", file.FileDownloadName);
        using var stream = new MemoryStream(file.FileContents);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        Assert.Equal("22/09/2026", Text(document.AcroForm?.Fields, "notification_date"));
        Assert.Equal("STORE-101", Text(document.AcroForm?.Fields, "store_id"));
        Assert.Equal("Test Store", Text(document.AcroForm?.Fields, "store_name"));
        Assert.Equal("Jamie Driver (Payroll: PAY-001)", Text(document.AcroForm?.Fields, "team_member"));
        Assert.Equal("Unable to attend", Text(document.AcroForm?.Fields, "reason_for_sickness"));
        Assert.Equal("12:00", Text(document.AcroForm?.Fields, "notification_time"));
        Assert.Equal("Phone call", Text(document.AcroForm?.Fields, "notification_method"));
        Assert.Equal("21/09/2026 18:00 - 22/09/2026 02:00", Text(document.AcroForm?.Fields, "original_shift"));
        Assert.Equal("Jamie Driver", Text(document.AcroForm?.Fields, "manager_name"));
        Assert.Equal(string.Empty, Text(document.AcroForm?.Fields, "manager_signature"));
    }

    [Fact]
    public async Task SavedStoreSettingsReplaceInitialConfigurationInDownloadedPdf()
    {
        using var fixture = new Fixture();
        await fixture.StoreSettings.SaveAsync(new StoreSettingsRequest
        {
            StoreId = "  ROI-204  ", StoreName = "  Dublin Central  "
        }, default);
        var submitted = await fixture.Submit();

        var file = Assert.IsType<FileContentResult>(
            await fixture.AdminController.DownloadPdf(submitted.Id, default));
        using var stream = new MemoryStream(file.FileContents);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        Assert.Equal("ROI-204", Text(document.AcroForm?.Fields, "store_id"));
        Assert.Equal("Dublin Central", Text(document.AcroForm?.Fields, "store_name"));
    }

    [Fact]
    public async Task StoreSettingsAreInitializedFromConfigurationUntilAdminChangesThem()
    {
        using var fixture = new Fixture();
        await fixture.StoreSettings.EnsureInitializedAsync(default);
        var initial = await fixture.StoreSettings.GetAsync(default);
        Assert.Equal("STORE-101", initial.StoreId);
        Assert.Equal("Test Store", initial.StoreName);
        Assert.Equal("STORE-101", (await fixture.Db.StoreSettings.SingleAsync()).StoreId);

        var saved = await fixture.StoreSettings.SaveAsync(new StoreSettingsRequest
        {
            StoreId = " STORE-202 ", StoreName = " Second Store "
        }, default);
        Assert.Equal("STORE-202", saved.StoreId);
        Assert.Equal("Second Store", saved.StoreName);
        Assert.Equal("STORE-202", (await fixture.Db.StoreSettings.SingleAsync()).StoreId);
    }

    [Fact]
    public async Task StoreSettingsEndpointRequiresAdminAndRejectsMissingValues()
    {
        using var fixture = new Fixture();
        var authorization = Assert.Single(typeof(AdminStoreSettingsController)
            .GetCustomAttributes(true).OfType<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.Admin, authorization.Policy);

        var controller = new AdminStoreSettingsController(
            fixture.StoreSettings, new StoreSettingsRequestValidator());
        Assert.IsType<BadRequestObjectResult>(await controller.Update(new StoreSettingsRequest(), default));
        Assert.Empty(await fixture.Db.StoreSettings.ToListAsync());
    }

    [Fact]
    public async Task StandaloneAdminDownloadUsesTheAdminUsername()
    {
        using var fixture = new Fixture();
        var submitted = await fixture.Submit();
        fixture.User.EmployeeId = null;
        await fixture.Db.SaveChangesAsync();

        var file = Assert.IsType<FileContentResult>(
            await fixture.AdminController.DownloadPdf(submitted.Id, default));
        using var stream = new MemoryStream(file.FileContents);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        Assert.Equal("absent-test", Text(document.AcroForm?.Fields, "manager_name"));
    }

    [Fact]
    public async Task EarlyMorningShiftWrappingMidnightPreservesBothCalendarDayOffsets()
    {
        using var fixture = new Fixture();
        fixture.OwnShift.StartTime = new TimeOnly(2, 0);
        fixture.OwnShift.FinishTime = new TimeOnly(1, 0);
        await fixture.Db.SaveChangesAsync();
        var form = await fixture.Submit();
        Assert.Equal(1, form.Shift.StartDayOffset);
        Assert.Equal(2, form.Shift.FinishDayOffset);
    }

    [Theory]
    [InlineData("time", "")]
    [InlineData("time", "24:00")]
    [InlineData("time", "12:60")]
    [InlineData("time", "9:00")]
    [InlineData("time", "12:30")]
    [InlineData("time", "12:34:56")]
    [InlineData("method", "   ")]
    [InlineData("reason", "   ")]
    [InlineData("method", null)]
    [InlineData("reason", null)]
    public async Task InvalidRequiredFieldsAreRejectedWithoutSaving(string field, string? value)
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        if (field == "time") request.NotificationTime = value!;
        if (field == "method") request.NotificationMethod = value!;
        if (field == "reason") request.CancellationReason = value!;
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Create(request, default));
        Assert.Empty(await fixture.Db.AbsentForms.ToListAsync());
    }

    [Fact]
    public void ValidationRequiresShiftAndLimitsTextToDatabaseCapacity()
    {
        var validator = new AbsentFormRequestValidator();
        var errors = validator.Validate(new AbsentFormCreateRequest
        {
            NotificationTime = "00:00", NotificationMethod = new string('a', 101),
            CancellationReason = new string('b', 2001)
        }).Errors.Select(error => error.PropertyName);
        Assert.Contains(nameof(AbsentFormCreateRequest.SavedRosterShiftId), errors);
        Assert.Contains(nameof(AbsentFormCreateRequest.NotificationMethod), errors);
        Assert.Contains(nameof(AbsentFormCreateRequest.CancellationReason), errors);
        Assert.DoesNotContain(nameof(AbsentFormCreateRequest.NotificationTime), errors);
    }

    [Theory]
    [InlineData("partial-manual")]
    [InlineData("saved-and-manual")]
    [InlineData("same-hours")]
    public void ValidationRejectsAmbiguousOrIncompleteManualShifts(string scenario)
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        if (scenario == "partial-manual")
        {
            request.SavedRosterShiftId = null;
            request.ShiftDate = new DateOnly(2026, 9, 25);
        }
        else
        {
            request.ShiftDate = new DateOnly(2026, 9, 25);
            request.ShiftStartTime = "18:00";
            request.ShiftFinishTime = scenario == "same-hours" ? "18:00" : "02:00";
            if (scenario == "same-hours") request.SavedRosterShiftId = null;
        }
        Assert.False(new AbsentFormRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void AdminEditValidationRequiresDateTimeMethodAndReason()
    {
        var errors = new AbsentFormUpdateRequestValidator().Validate(new AbsentFormUpdateRequest
        {
            SavedRosterShiftId = Guid.Empty,
            NotificationDate = default,
            NotificationTime = "9:00",
            NotificationMethod = " ",
            CancellationReason = " "
        }).Errors.Select(error => error.PropertyName).ToList();
        Assert.Contains(nameof(AbsentFormUpdateRequest.SavedRosterShiftId), errors);
        Assert.Contains(nameof(AbsentFormUpdateRequest.NotificationDate), errors);
        Assert.Contains(nameof(AbsentFormUpdateRequest.NotificationTime), errors);
        Assert.Contains(nameof(AbsentFormUpdateRequest.NotificationMethod), errors);
        Assert.Contains(nameof(AbsentFormUpdateRequest.CancellationReason), errors);
    }

    [Fact]
    public void PersonalEndpointsRequireDriverWhileManagementReadsAndDownloadsAllowManagers()
    {
        var driver = Assert.Single(typeof(AbsentController).GetCustomAttributes(true).OfType<AuthorizeAttribute>());
        Assert.Equal(RoleNames.Driver, driver.Roles);
        Assert.Null(typeof(AbsentController).GetMethod("DownloadPdf"));
        var admin = Assert.Single(typeof(AdminAbsentController).GetCustomAttributes(true).OfType<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.Manager, admin.Policy);
        Assert.Equal(AuthorizationPolicies.Admin,
            Assert.Single(typeof(AdminAbsentController).GetMethod(nameof(AdminAbsentController.Update))!
                .GetCustomAttributes(true).OfType<AuthorizeAttribute>()).Policy);
        Assert.Equal(AuthorizationPolicies.Admin,
            Assert.Single(typeof(AdminAbsentController).GetMethod(nameof(AdminAbsentController.Delete))!
                .GetCustomAttributes(true).OfType<AuthorizeAttribute>()).Policy);
    }

    private static T Read<T>(IActionResult result) => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result).Value);

    private static string Text(PdfAcroField.PdfAcroFieldCollection? fields, string name) =>
        Assert.IsType<PdfTextField>(fields?[name]).Text;

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 21, 23, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        private readonly UserManager<ApplicationUser> userManager;
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public TestClock Clock { get; } = new();
        public Employee Employee { get; } = new()
        {
            Id = Guid.NewGuid(), FirstName = "Jamie", LastName = "Driver", PayrollNumber = "PAY-001", IsActive = true
        };
        public Employee OtherEmployee { get; } = new()
        {
            Id = Guid.NewGuid(), FirstName = "Alex", LastName = "Driver", IsActive = true
        };
        public ApplicationUser User { get; }
        public RosterShift OwnShift { get; }
        public RosterShift OtherShift { get; }
        public RosterShift InsideShift { get; }
        public RosterShift FutureShift { get; }
        public AbsentFormService Service { get; }
        public StoreSettingsService StoreSettings { get; }
        public AbsentPdfService PdfService { get; }
        public AbsentController Controller { get; }
        public AdminAbsentController AdminController { get; }

        public Fixture()
        {
            Db.Employees.AddRange(Employee, OtherEmployee);
            User = new ApplicationUser { Id = Guid.NewGuid(), UserName = "absent-test", EmployeeId = Employee.Id };
            Db.Users.Add(User);
            var role = new IdentityRole<Guid>(RoleNames.Driver) { Id = Guid.NewGuid(), NormalizedName = "DRIVER" };
            Db.Roles.Add(role);
            Db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = User.Id, RoleId = role.Id });
            var monday = new DateOnly(2026, 9, 21);
            var plan = new RosterPlan { Id = Guid.NewGuid(), WeekStart = monday, RosterKind = RosterKinds.Drivers };
            var inside = new RosterPlan { Id = Guid.NewGuid(), WeekStart = monday, RosterKind = RosterKinds.Inside };
            var future = new RosterPlan { Id = Guid.NewGuid(), WeekStart = monday.AddDays(7), RosterKind = RosterKinds.Drivers };
            Db.RosterPlans.AddRange(plan, inside, future);
            OwnShift = NewShift(plan, Employee);
            OtherShift = NewShift(plan, OtherEmployee);
            InsideShift = NewShift(inside, Employee);
            FutureShift = NewShift(future, Employee);
            Db.RosterShifts.AddRange(OwnShift, OtherShift, InsideShift, FutureShift);
            Db.SaveChanges();
            userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(Db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            Service = new AbsentFormService(Db, Clock);
            StoreSettings = new StoreSettingsService(Db, Options.Create(new AbsencePdfOptions
            {
                StoreId = "STORE-101", StoreName = "Test Store"
            }));
            PdfService = new AbsentPdfService(StoreSettings);
            AdminController = new AdminAbsentController(
                Service, PdfService, Db, userManager, new AbsentFormUpdateRequestValidator())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([
                            new Claim(ClaimTypes.NameIdentifier, User.Id.ToString())
                        ], "TestAuthentication"))
                    }
                }
            };
            Controller = new AbsentController(Db, userManager, Service, new AbsentFormRequestValidator())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([
                            new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()),
                            new Claim(ClaimTypes.Role, RoleNames.Driver)
                        ], "TestAuthentication"))
                    }
                }
            };
        }

        public AbsentFormCreateRequest Request(Guid? shiftId = null) => new()
        {
            SavedRosterShiftId = shiftId ?? OwnShift.Id,
            NotificationDate = new DateOnly(2026, 9, 22), NotificationTime = "12:00",
            NotificationMethod = "Phone call", CancellationReason = "Unable to attend"
        };

        public async Task<AbsentFormResponse> Submit(string time = "12:00", DateOnly? notificationDate = null)
        {
            var request = Request();
            request.NotificationTime = time;
            if (notificationDate is not null) request.NotificationDate = notificationDate.Value;
            return Read<AbsentFormResponse>(await Controller.Create(request, default));
        }

        private static RosterShift NewShift(RosterPlan plan, Employee employee) => new()
        {
            Id = Guid.NewGuid(), RosterPlanId = plan.Id, EmployeeId = employee.Id, Date = plan.WeekStart,
            StartTime = new TimeOnly(18, 0), FinishTime = new TimeOnly(2, 0)
        };

        public void Dispose()
        {
            userManager.Dispose();
            Db.Dispose();
            services.Dispose();
        }
    }
}
