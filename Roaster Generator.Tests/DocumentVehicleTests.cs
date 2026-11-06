using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class DocumentVehicleTests : IDisposable
{
    private const string OldCar = "241-D-111";
    private const string NewCar = "251-D-222";
    private static readonly DateOnly Future = new(2030, 1, 1);

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DocumentVehicleService vehicles;
    private readonly DocumentDraftService drafts;
    private readonly FakeAnalyser analyser = new();
    private readonly Employee driver = new()
    {
        Id = Guid.NewGuid(), FirstName = "Anna", LastName = "Driver", DriverProfile = new DriverProfile()
    };
    private readonly DocumentActor actor = new(Guid.NewGuid(), "anna", false);
    private readonly Dictionary<string, DocumentType> types;

    public DocumentVehicleTests()
    {
        var protector = new DocumentFileProtector(Options.Create(new DocumentStorageOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
        vehicles = new DocumentVehicleService(db, TimeProvider.System);
        drafts = new DocumentDraftService(db, analyser, protector, vehicles, TimeProvider.System);
        types = new[] { DocumentTypeCodes.Obu, DocumentTypeCodes.Nct, DocumentTypeCodes.MotorTax, DocumentTypeCodes.Insurance }
            .Select((code, index) => new DocumentType
            {
                Id = Guid.NewGuid(), Code = code, Name = code, SortOrder = index,
                ExpiryRule = code == DocumentTypeCodes.Obu ? DocumentExpiryRule.None : DocumentExpiryRule.Printed
            })
            .ToDictionary(type => type.Code);
        db.DocumentTypes.AddRange(types.Values);
        foreach (var category in new[] { DocumentCategory.CarOwn, DocumentCategory.CarCompany, DocumentCategory.EBikeOwn })
            db.DocumentRequirements.AddRange(types.Values.Select(type =>
                new DocumentRequirement { Category = category, DocumentTypeId = type.Id }));
        db.Employees.Add(driver);
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Driver };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "1", EmployeeId = driver.Id };
        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        db.SaveChanges();
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task FirstVehicleDocumentSetsTheCurrentRegistration()
    {
        await Upload(DocumentTypeCodes.Obu, OldCar);

        Assert.Equal(OldCar, (await vehicles.GetAsync(driver.Id, default)).Registration);
    }

    [Fact]
    public async Task MatchingDocumentsAreNotFlaggedIgnoringFormatting()
    {
        await Upload(DocumentTypeCodes.Obu, OldCar);

        Assert.False((await Upload(DocumentTypeCodes.Nct, "241 d 111")).NeedsReview);
    }

    [Fact]
    public async Task DifferentRegistrationPromptsInsteadOfWarning()
    {
        await Upload(DocumentTypeCodes.Obu, OldCar);
        analyser.Result = Analysis(DocumentTypeCodes.Nct, NewCar);

        var response = await Analyse();

        Assert.Equal(OldCar, response.VehiclePrompt!.CurrentRegistration);
        Assert.Equal(NewCar, response.VehiclePrompt.DetectedRegistration);
        Assert.DoesNotContain(response.Warnings, warning => warning.Contains("registration"));
    }

    [Fact]
    public async Task AnsweringNoUploadsItFlaggedAndShowsWrongVehicle()
    {
        await Upload(DocumentTypeCodes.Obu, OldCar);

        var saved = await Upload(DocumentTypeCodes.Nct, NewCar);

        Assert.True(saved.NeedsReview);
        Assert.Equal(DocumentRowState.WrongVehicle, await StateOf(DocumentTypeCodes.Nct));
    }

    [Fact]
    public async Task ClearingAReviewDoesNotHideAMismatch()
    {
        await Upload(DocumentTypeCodes.Obu, OldCar);
        await Upload(DocumentTypeCodes.Nct, NewCar);
        var document = await db.EmployeeDocuments.SingleAsync(item => item.VehicleRegistration == NewCar);
        document.ReviewStatus = DocumentReviewStatus.Clear;
        document.ReviewReasons = [];
        await db.SaveChangesAsync();

        Assert.Equal(DocumentRowState.WrongVehicle, await StateOf(DocumentTypeCodes.Nct));
    }

    [Fact]
    public async Task CarChangeMarksOldDocumentsAndClosesWhenAllMatch()
    {
        await UploadAll(OldCar);

        await vehicles.StartChangeAsync(driver, actor, NewCar, default);

        var state = await vehicles.GetAsync(driver.Id, default);
        Assert.Equal(NewCar, state.Registration);
        Assert.Equal(OldCar, state.OpenChange!.OldRegistration);
        Assert.Equal(DocumentRowState.NeedsVehicleUpdate, await StateOf(DocumentTypeCodes.Nct));

        await Upload(DocumentTypeCodes.Nct, NewCar);
        await Upload(DocumentTypeCodes.MotorTax, NewCar);
        await Upload(DocumentTypeCodes.Insurance, NewCar);
        Assert.NotNull((await vehicles.GetAsync(driver.Id, default)).OpenChange);

        var last = await Upload(DocumentTypeCodes.Obu, NewCar);

        Assert.False(last.NeedsReview);
        Assert.Null((await vehicles.GetAsync(driver.Id, default)).OpenChange);
        var change = await db.VehicleChanges.SingleAsync();
        Assert.Equal(VehicleChangeStatus.Completed, change.Status);
        Assert.NotNull(change.ClosedAtUtc);
        Assert.Contains(db.DocumentAuditEvents, item => item.Action == DocumentAuditAction.VehicleCompleted);
    }

    [Fact]
    public async Task UploadForTheOldCarDuringAChangeIsFlagged()
    {
        await UploadAll(OldCar);
        await vehicles.StartChangeAsync(driver, actor, NewCar, default);

        Assert.True((await Upload(DocumentTypeCodes.Nct, OldCar)).NeedsReview);
    }

    [Fact]
    public async Task NoPromptWhileAChangeIsOpen()
    {
        await UploadAll(OldCar);
        await vehicles.StartChangeAsync(driver, actor, NewCar, default);
        analyser.Result = Analysis(DocumentTypeCodes.Nct, OldCar);

        var response = await Analyse();

        Assert.Null(response.VehiclePrompt);
        Assert.Contains(response.Warnings, warning => warning.Contains("registration"));
    }

    [Fact]
    public async Task StartingAgainCorrectsTheOpenChange()
    {
        await UploadAll(OldCar);
        await vehicles.StartChangeAsync(driver, actor, "wrong", default);

        await vehicles.StartChangeAsync(driver, actor, NewCar, default);

        var change = await db.VehicleChanges.SingleAsync();
        Assert.Equal(OldCar, change.OldRegistration);
        Assert.Equal(NewCar, change.NewRegistration);
    }

    [Fact]
    public async Task CancellingRestoresThePreviousRegistration()
    {
        await UploadAll(OldCar);
        await vehicles.StartChangeAsync(driver, actor, NewCar, default);

        Assert.True(await vehicles.CancelChangeAsync(driver, actor, default));

        var state = await vehicles.GetAsync(driver.Id, default);
        Assert.Equal(OldCar, state.Registration);
        Assert.Null(state.OpenChange);
        Assert.Equal(DocumentRowState.Valid, await StateOf(DocumentTypeCodes.Nct));
        Assert.Equal(VehicleChangeStatus.Cancelled, (await db.VehicleChanges.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancellingWithNoChangeOpenReportsNothing()
    {
        Assert.False(await vehicles.CancelChangeAsync(driver, actor, default));
    }

    [Fact]
    public async Task SameRegistrationIsNotAChange()
    {
        await UploadAll(OldCar);

        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => vehicles.StartChangeAsync(driver, actor, "241 d 111", default));
    }

    [Theory]
    [InlineData(DriverType.Car, false)]
    [InlineData(DriverType.EBike, true)]
    public async Task OnlyOwnCarsAndMopedsAreTracked(DriverType type, bool isOwn)
    {
        var profile = await db.DriverProfiles.SingleAsync();
        profile.DriverType = type;
        profile.IsOwn = isOwn;
        await db.SaveChangesAsync();

        await Upload(DocumentTypeCodes.Obu, OldCar);
        var saved = await Upload(DocumentTypeCodes.Nct, NewCar);

        Assert.False((await vehicles.GetAsync(driver.Id, default)).TracksVehicle);
        Assert.Empty(db.EmployeeVehicles);
        Assert.False(saved.NeedsReview);
        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => vehicles.StartChangeAsync(driver, actor, NewCar, default));
    }

    [Fact]
    public async Task OwnMopedIsTracked()
    {
        (await db.DriverProfiles.SingleAsync()).DriverType = DriverType.Moped;
        await db.SaveChangesAsync();

        Assert.True((await vehicles.GetAsync(driver.Id, default)).TracksVehicle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    public void RegistrationIsValidated(string registration)
    {
        Assert.False(new VehicleChangeRequestValidator().Validate(new VehicleChangeRequest { Registration = registration }).IsValid);
    }

    [Fact]
    public void WrongVehicleCountsAsOutstanding()
    {
        var type = new DocumentType { Code = DocumentTypeCodes.Nct };
        DocumentChecklistRow[] rows =
        [
            new(type, DocumentRowState.WrongVehicle, true, null),
            new(type, DocumentRowState.NeedsVehicleUpdate, true, null),
            new(type, DocumentRowState.NotUploaded, true, null),
            new(type, DocumentRowState.Valid, true, null),
            new(type, DocumentRowState.WrongVehicle, false, null)
        ];

        Assert.Equal(3, DocumentRequirementResolver.VehicleDocumentsOutstanding(rows));
    }

    private async Task UploadAll(string registration)
    {
        foreach (var code in types.Keys) await Upload(code, registration);
    }

    private async Task<DocumentSubmitResponse> Upload(string code, string registration)
    {
        analyser.Result = Analysis(code, registration);
        var draft = await Analyse();
        return (await drafts.SubmitAsync(driver, actor, draft.DraftId, new DocumentSubmitRequest
        {
            DocumentTypeId = types[code].Id,
            HolderName = "Anna Driver",
            VehicleRegistration = registration,
            ExpiryDate = code == DocumentTypeCodes.Obu ? null : Future
        }, default))!;
    }

    private Task<DocumentAnalysisResponse> Analyse() => drafts.AnalyseAsync(
        driver, actor, [new ValidatedDocumentFile([0xFF, 0xD8, 0xFF], "a.jpg", "image/jpeg")], null, default);

    private static DocumentAnalysis Analysis(string code, string registration) => new()
    {
        DocumentType = code,
        Readable = true,
        Fields = new DocumentFields
        {
            HolderName = "Anna Driver",
            VehicleRegistration = registration,
            ExpiryDate = code == DocumentTypeCodes.Obu ? null : Future
        }
    };

    private async Task<DocumentRowState> StateOf(string code)
    {
        var requirements = await db.DocumentRequirements.Include(item => item.DocumentType).ToListAsync();
        var documents = await db.EmployeeDocuments.Include(item => item.DocumentType).ToListAsync();
        var state = await vehicles.GetAsync(driver.Id, default);
        return DocumentRequirementResolver.Resolve(
                [DocumentCategory.CarOwn], requirements, types.Values, documents, new DateOnly(2026, 11, 6), state.Context)
            .Single(row => row.DocumentType.Code == code).State;
    }

    private sealed class FakeAnalyser : IDocumentAnalyser
    {
        public DocumentAnalysis? Result { get; set; }
        public bool IsConfigured => true;

        public Task<DocumentAnalysis?> AnalyseAsync(
            IReadOnlyList<ValidatedDocumentFile> files, IReadOnlyList<DocumentCatalogueEntry> catalogue, CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<IReadOnlyList<string>?> CheckSignedFormAsync(
            byte[] pdf, string formName, string employeeName, IReadOnlyList<string> answers, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>?>([]);
    }
}
