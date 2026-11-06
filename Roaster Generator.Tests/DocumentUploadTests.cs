using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed class DocumentDraftServiceTests : IDisposable
{
    private static readonly DateOnly Future = new(2030, 1, 1);
    private static readonly byte[] Content = [0xFF, 0xD8, 0xFF, 1, 2, 3];

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock time = new();
    private readonly FakeAnalyser analyser = new();
    private readonly DocumentFileProtector protector = new(Options.Create(new DocumentStorageOptions
    {
        EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly DocumentDraftService service;
    private readonly Employee employee = new() { Id = Guid.NewGuid(), FirstName = "John", LastName = "Smith" };
    private readonly DocumentType passport = Type(DocumentTypeCodes.Passport, DocumentExpiryRule.Printed);
    private readonly DocumentType points = Type(DocumentTypeCodes.PenaltyPoints, DocumentExpiryRule.PenaltyPoints);
    private readonly DocumentType nct = Type(DocumentTypeCodes.Nct, DocumentExpiryRule.Printed);
    private readonly DocumentActor actor = new(Guid.NewGuid(), "john", false);

    public DocumentDraftServiceTests()
    {
        service = new DocumentDraftService(db, analyser, protector, new DocumentVehicleService(db, time), time);
        db.Employees.Add(employee);
        db.DocumentTypes.AddRange(passport, points, nct);
        db.SaveChanges();
        analyser.Result = Clean(passport);
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task AnalysisEncryptsFilesAndReturnsWhatWasRead()
    {
        var response = await Analyse();

        Assert.True(response.IsAnalysed);
        Assert.Equal(passport.Id, response.DocumentTypeId);
        Assert.Equal("JOHN SMITH", response.Fields.HolderName);
        Assert.Empty(response.Warnings);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        Assert.Equal(response.DraftId, file.DraftId);
        Assert.NotEqual(Content, file.EncryptedContent);
        Assert.Equal(Content, protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId)));
        Assert.Equal(time.GetUtcNow() + DocumentDraftService.DraftLifetime, response.ExpiresAtUtc);
    }

    [Fact]
    public async Task AnalysisOutageStillGivesADraftAndAWarning()
    {
        analyser.Result = null;

        var response = await Analyse(passport.Id);

        Assert.False(response.IsAnalysed);
        Assert.Equal(passport.Id, response.DocumentTypeId);
        Assert.Contains("could not be checked", Assert.Single(response.Warnings));
    }

    [Fact]
    public async Task MissingEncryptionKeyStopsBeforeCallingTheAnalyser()
    {
        var unconfigured = new DocumentDraftService(
            db, analyser, new DocumentFileProtector(Options.Create(new DocumentStorageOptions())),
            new DocumentVehicleService(db, time), time);

        await Assert.ThrowsAsync<DocumentUnavailableException>(
            () => unconfigured.AnalyseAsync(employee, actor, Files(), null, default));
        Assert.Equal(0, analyser.Calls);
    }

    [Fact]
    public async Task NewAnalysisReplacesTheActorsEarlierDraft()
    {
        var first = await Analyse();

        var second = await Analyse();

        Assert.Equal(second.DraftId, (await db.DocumentDrafts.SingleAsync()).Id);
        Assert.Equal(second.DraftId, (await db.EmployeeDocumentFiles.SingleAsync()).DraftId);
        Assert.Null(await service.SubmitAsync(employee, actor, first.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task WarnsAboutCheckFailuresAndAWrongRowChoice()
    {
        analyser.Result = Clean(passport);
        analyser.Result.Fields.ExpiryDate = new DateOnly(2020, 1, 1);
        analyser.Result.Fields.HolderName = "Jane Doe";

        var response = await Analyse(nct.Id);

        Assert.Equal(passport.Id, response.DocumentTypeId);
        Assert.Contains(response.Warnings, warning => warning.Contains("expired"));
        Assert.Contains(response.Warnings, warning => warning.Contains("does not match"));
        Assert.Contains(response.Warnings, warning => warning.Contains("looks like"));
    }

    [Fact]
    public async Task CleanSubmissionIsClearAndMovesTheFile()
    {
        var draft = await Analyse();

        var saved = await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default);

        Assert.False(saved!.NeedsReview);
        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.Clear, document.ReviewStatus);
        Assert.Empty(document.ReviewReasons);
        Assert.Equal(Future, document.ExpiryDate);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        Assert.Equal(document.Id, file.DocumentId);
        Assert.Null(file.DraftId);
        Assert.Empty(db.DocumentDrafts);
        Assert.Equal(
            [DocumentAuditAction.Analysed, DocumentAuditAction.Uploaded],
            await db.DocumentAuditEvents.Select(item => item.Action).OrderBy(action => action).ToListAsync());
    }

    [Fact]
    public async Task EditedFieldFlagsForReview()
    {
        var draft = await Analyse();
        var request = Request(passport);
        request.ExpiryDate = new DateOnly(2031, 1, 1);

        var saved = await service.SubmitAsync(employee, actor, draft.DraftId, request, default);

        Assert.True(saved!.NeedsReview);
        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.NeedsReview, document.ReviewStatus);
        Assert.Contains(document.ReviewReasons, reason => reason.Contains("expiry date"));
    }

    [Fact]
    public async Task WhitespaceAndCaseAreNotEdits()
    {
        var draft = await Analyse();
        var request = Request(passport);
        request.ReferenceNumber = "  ab123 ";

        Assert.False((await service.SubmitAsync(employee, actor, draft.DraftId, request, default))!.NeedsReview);
    }

    [Fact]
    public async Task ChangedTypeFlagsForReview()
    {
        var draft = await Analyse();
        var request = Request(nct);

        await service.SubmitAsync(employee, actor, draft.DraftId, request, default);

        Assert.Contains((await db.EmployeeDocuments.SingleAsync()).ReviewReasons, reason => reason.Contains("document type"));
    }

    [Fact]
    public async Task AnalysisProblemsFlagForReviewButDoNotBlock()
    {
        analyser.Result!.Problems = ["Glare over the expiry date."];
        var draft = await Analyse();

        var saved = await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default);

        Assert.True(saved!.NeedsReview);
        Assert.Contains("Glare over the expiry date.", (await db.EmployeeDocuments.SingleAsync()).ReviewReasons);
    }

    [Fact]
    public async Task ExpiredDocumentCanStillBeUploadedButIsFlagged()
    {
        analyser.Result!.Fields.ExpiryDate = new DateOnly(2020, 1, 1);
        var draft = await Analyse();
        var request = Request(passport);
        request.ExpiryDate = new DateOnly(2020, 1, 1);

        Assert.True((await service.SubmitAsync(employee, actor, draft.DraftId, request, default))!.NeedsReview);
    }

    [Fact]
    public async Task OutageUploadIsFlagged()
    {
        analyser.Result = null;
        var draft = await Analyse(passport.Id);

        var saved = await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default);

        Assert.True(saved!.NeedsReview);
    }

    [Fact]
    public async Task AdministratorUploadIsNotFlagged()
    {
        var admin = new DocumentActor(Guid.NewGuid(), "admin", true);
        analyser.Result = null;
        var draft = await service.AnalyseAsync(employee, admin, Files(), passport.Id, default);

        var saved = await service.SubmitAsync(employee, admin, draft.DraftId, Request(passport), default);

        Assert.False(saved!.NeedsReview);
    }

    [Fact]
    public async Task ReplacementDeletesTheOldDocumentAndItsFiles()
    {
        await service.SubmitAsync(employee, actor, (await Analyse()).DraftId, Request(passport), default);
        var first = await db.EmployeeDocuments.AsNoTracking().SingleAsync();

        var saved = await service.SubmitAsync(employee, actor, (await Analyse()).DraftId, Request(passport), default);

        Assert.True(saved!.ReplacedExisting);
        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.NotEqual(first.Id, document.Id);
        Assert.Equal(document.Id, (await db.EmployeeDocumentFiles.SingleAsync()).DocumentId);
        Assert.Contains(await db.DocumentAuditEvents.ToListAsync(),
            item => item.Action == DocumentAuditAction.Replaced && item.DocumentId == first.Id);
    }

    [Fact]
    public async Task PenaltyPointsExpiryIsCalculated()
    {
        analyser.Result = new DocumentAnalysis { DocumentType = points.Code, Readable = true };
        analyser.Result.Fields.SigningDate = new DateOnly(2026, 10, 1);
        analyser.Result.Fields.PenaltyPoints = 4;
        var draft = await Analyse();
        var request = new DocumentSubmitRequest
        {
            DocumentTypeId = points.Id,
            SigningDate = new DateOnly(2026, 10, 1),
            PenaltyPoints = 4
        };

        await service.SubmitAsync(employee, actor, draft.DraftId, request, default);

        Assert.Equal(new DateOnly(2026, 11, 1), (await db.EmployeeDocuments.SingleAsync()).ExpiryDate);
    }

    [Fact]
    public async Task ExpiredDraftCannotBeSubmitted()
    {
        var draft = await Analyse();
        time.Advance(DocumentDraftService.DraftLifetime);

        Assert.Null(await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task AnotherEmployeesDraftIsInvisible()
    {
        var draft = await Analyse();
        var other = new Employee { Id = Guid.NewGuid() };
        var otherActor = new DocumentActor(Guid.NewGuid(), "other", false);

        Assert.Null(await service.SubmitAsync(other, otherActor, draft.DraftId, Request(passport), default));
        Assert.False(await service.DiscardAsync(other, otherActor, draft.DraftId, default));
        Assert.Single(db.DocumentDrafts);
    }

    [Fact]
    public async Task EmployeeCannotConfirmAnAdministratorsDraftForThem()
    {
        var admin = new DocumentActor(Guid.NewGuid(), "admin", true);
        var draft = await service.AnalyseAsync(employee, admin, Files(), null, default);

        Assert.Null(await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task DiscardRemovesTheFiles()
    {
        var draft = await Analyse();

        Assert.True(await service.DiscardAsync(employee, actor, draft.DraftId, default));

        Assert.Empty(db.DocumentDrafts);
        Assert.Empty(db.EmployeeDocumentFiles);
    }

    [Fact]
    public async Task OneSideOfATwoSidedCardIsRefused()
    {
        await RequireBothSides();
        var draft = await Analyse();

        Assert.False(draft.HasBothSides);
        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task TwoPagesCountAsBothSides()
    {
        await RequireBothSides();
        var draft = await service.AnalyseAsync(employee, actor, [.. Files(), .. Files()], null, default);

        Assert.True(draft.HasBothSides);
        Assert.NotNull(await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task TwoPagePdfCountsAsBothSides()
    {
        await RequireBothSides();
        ValidatedDocumentFile[] pdf = [new(Content, "card.pdf", "application/pdf", 2)];
        var draft = await service.AnalyseAsync(employee, actor, pdf, null, default);

        Assert.True(draft.HasBothSides);
    }

    [Fact]
    public async Task OnePhotoShowingBothSidesIsAccepted()
    {
        await RequireBothSides();
        analyser.Result!.BothSides = true;
        var draft = await Analyse();

        Assert.NotNull(await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task OutageStillAllowsOneSideButFlagsIt()
    {
        await RequireBothSides();
        analyser.Result = null;
        var draft = await Analyse(passport.Id);

        await service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default);

        Assert.Contains((await db.EmployeeDocuments.SingleAsync()).ReviewReasons, reason => reason.Contains("one side"));
    }

    [Fact]
    public async Task AdministratorCanUploadOneSide()
    {
        await RequireBothSides();
        var admin = new DocumentActor(Guid.NewGuid(), "admin", true);
        var draft = await service.AnalyseAsync(employee, admin, Files(), null, default);

        Assert.False((await service.SubmitAsync(employee, admin, draft.DraftId, Request(passport), default))!.NeedsReview);
    }

    [Fact]
    public async Task TypeWithoutTheRuleAcceptsOneSide()
    {
        Assert.NotNull(await service.SubmitAsync(employee, actor, (await Analyse()).DraftId, Request(passport), default));
    }

    private async Task RequireBothSides()
    {
        passport.RequiresBothSides = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmployeesCannotUploadASignedType()
    {
        passport.Kind = DocumentKind.Sign;
        await db.SaveChangesAsync();
        var draft = await Analyse();

        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task AdministratorsCanUploadAPaperCopyOfASignedType()
    {
        passport.Kind = DocumentKind.Sign;
        await db.SaveChangesAsync();
        var admin = new DocumentActor(Guid.NewGuid(), "admin", true);
        var draft = await service.AnalyseAsync(employee, admin, Files(), passport.Id, default);

        Assert.NotNull(await service.SubmitAsync(employee, admin, draft.DraftId, Request(passport), default));
    }

    [Fact]
    public async Task SignedTypesAreNotOfferedToTheAnalysisForEmployees()
    {
        passport.Kind = DocumentKind.Sign;
        await db.SaveChangesAsync();

        await Analyse();

        Assert.DoesNotContain(analyser.LastCatalogue, entry => entry.Code == passport.Code);
    }

    [Fact]
    public async Task InactiveTypeIsRejected()
    {
        var draft = await Analyse();
        passport.IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => service.SubmitAsync(employee, actor, draft.DraftId, Request(passport), default));
    }

    private Task<DocumentAnalysisResponse> Analyse(Guid? requestedTypeId = null) =>
        service.AnalyseAsync(employee, actor, Files(), requestedTypeId, default);

    private static ValidatedDocumentFile[] Files() => [new(Content, "front.jpg", "image/jpeg")];

    private static DocumentSubmitRequest Request(DocumentType type) => new()
    {
        DocumentTypeId = type.Id,
        HolderName = "JOHN SMITH",
        ReferenceNumber = "AB123",
        IssuingCountry = "IE",
        ExpiryDate = Future
    };

    private static DocumentAnalysis Clean(DocumentType type) => new()
    {
        DocumentType = type.Code,
        Readable = true,
        Summary = "A passport.",
        Fields = new DocumentFields
        {
            HolderName = "JOHN SMITH", ReferenceNumber = "AB123", IssuingCountry = "IE", ExpiryDate = Future
        }
    };

    private static DocumentType Type(string code, DocumentExpiryRule rule) =>
        new() { Id = Guid.NewGuid(), Code = code, Name = code, ExpiryRule = rule };

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan span) => now += span;
    }

    private sealed class FakeAnalyser : IDocumentAnalyser
    {
        public DocumentAnalysis? Result { get; set; }
        public int Calls { get; private set; }
        public IReadOnlyList<DocumentCatalogueEntry> LastCatalogue { get; private set; } = [];
        public bool IsConfigured => true;

        public Task<DocumentAnalysis?> AnalyseAsync(
            IReadOnlyList<ValidatedDocumentFile> files, IReadOnlyList<DocumentCatalogueEntry> catalogue, CancellationToken cancellationToken)
        {
            Calls++;
            LastCatalogue = catalogue;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<string>?> CheckSignedFormAsync(
            byte[] pdf, string formName, string employeeName, IReadOnlyList<string> answers, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>?>([]);
    }
}

public sealed class DocumentAnalysisParserTests
{
    private static readonly string[] Codes = [DocumentTypeCodes.Passport];

    [Fact]
    public void ReadsValidOutput()
    {
        var analysis = DocumentAnalysisParser.Parse("""
            {"documentType":"passport","readable":true,"holderName":" JOHN SMITH ","referenceNumber":"AB1",
             "issuingCountry":"ie","issueDate":"2020-01-31","expiryDate":"2030-01-31","stamp":null,
             "vehicleRegistration":null,"signingDate":null,"penaltyPoints":2,"summary":"A passport.","problems":["Glare"]}
            """, Codes);

        Assert.Equal(DocumentTypeCodes.Passport, analysis!.DocumentType);
        Assert.True(analysis.Readable);
        Assert.Equal("JOHN SMITH", analysis.Fields.HolderName);
        Assert.Equal("IE", analysis.Fields.IssuingCountry);
        Assert.Equal(new DateOnly(2030, 1, 31), analysis.Fields.ExpiryDate);
        Assert.Equal(2, analysis.Fields.PenaltyPoints);
        Assert.Equal(["Glare"], analysis.Problems);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("")]
    public void InvalidOutputIsNoAnalysis(string json)
    {
        Assert.Null(DocumentAnalysisParser.Parse(json, Codes));
    }

    [Fact]
    public void UnknownCodeBecomesUnknown()
    {
        Assert.Equal(DocumentAnalysis.UnknownType,
            DocumentAnalysisParser.Parse("""{"documentType":"made-up"}""", Codes)!.DocumentType);
    }

    [Theory]
    [InlineData("31/01/2030")]
    [InlineData("2030-13-01")]
    [InlineData("soon")]
    public void DatesMustBeIso(string date)
    {
        Assert.Null(DocumentAnalysisParser.Parse($$"""{"expiryDate":"{{date}}"}""", Codes)!.Fields.ExpiryDate);
    }

    [Theory]
    [InlineData("""{"issuingCountry":"Ireland"}""")]
    [InlineData("""{"issuingCountry":"I1"}""")]
    public void CountryMustBeACode(string json)
    {
        Assert.Null(DocumentAnalysisParser.Parse(json, Codes)!.Fields.IssuingCountry);
    }

    [Theory]
    [InlineData("""{"penaltyPoints":-1}""")]
    [InlineData("""{"penaltyPoints":101}""")]
    [InlineData("""{"penaltyPoints":"3"}""")]
    public void PointsMustBeARealisticNumber(string json)
    {
        Assert.Null(DocumentAnalysisParser.Parse(json, Codes)!.Fields.PenaltyPoints);
    }

    [Fact]
    public void LimitsTextFromTheDocument()
    {
        var longText = new string('x', 500);
        var problems = string.Join(",", Enumerable.Repeat($"\"{longText}\"", 9));

        var analysis = DocumentAnalysisParser.Parse(
            $$"""{"holderName":"{{longText}}","summary":"{{longText}}","problems":[{{problems}}]}""", Codes)!;

        Assert.Equal(200, analysis.Fields.HolderName!.Length);
        Assert.Equal(300, analysis.Summary!.Length);
        Assert.Equal(5, analysis.Problems.Count);
        Assert.All(analysis.Problems, problem => Assert.Equal(200, problem.Length));
    }

    [Fact]
    public void ReadsWhetherBothSidesWereSeen()
    {
        Assert.True(DocumentAnalysisParser.Parse("""{"bothSides":true}""", Codes)!.BothSides);
        Assert.False(DocumentAnalysisParser.Parse("{}", Codes)!.BothSides);
    }

    [Fact]
    public void MissingReadableMeansUnreadable()
    {
        Assert.False(DocumentAnalysisParser.Parse("{}", Codes)!.Readable);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    [InlineData(null)]
    public void OnlyACompletedResponseIsUsable(string? stopReason)
    {
        Assert.Null(ClaudeDocumentAnalyser.ReadText(stopReason, ["{}"]));
    }

    [Fact]
    public void CompletedResponseJoinsItsText()
    {
        Assert.Equal("{}", ClaudeDocumentAnalyser.ReadText("end_turn", ["{", "}"]));
        Assert.Null(ClaudeDocumentAnalyser.ReadText("end_turn", []));
    }
}

public sealed class DocumentEndpointAccessTests
{
    [Fact]
    public void EmployeesHaveNoRouteToFileContents()
    {
        var returnsFiles = typeof(DocumentsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
            .Where(route => route.Template?.Contains("files", StringComparison.OrdinalIgnoreCase) == true);

        Assert.Empty(returnsFiles);
        Assert.Equal(["GET", "POST", "POST", "DELETE", "POST", "DELETE", "GET", "GET", "POST"],
            typeof(DocumentsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
                .SelectMany(route => route.HttpMethods));
    }

    [Fact]
    public void OnBehalfUploadsRequireTheAdminPolicy()
    {
        var attribute = typeof(AdminEmployeeDocumentsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(AuthorizationPolicies.Admin, attribute!.Policy);
    }

    [Fact]
    public void EmployeeEndpointsRequireSignIn()
    {
        Assert.NotNull(typeof(DocumentsController).GetCustomAttribute<AuthorizeAttribute>());
    }
}
