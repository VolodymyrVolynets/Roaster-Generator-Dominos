using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed class DocumentSigningTests : IDisposable
{
    // A 1x1 PNG, enough to be placed on a form.
    private const string Signature =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DocumentFileProtector protector = new(Options.Create(new DocumentStorageOptions
    {
        EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly DocumentSigningService service;
    private readonly FakeAnalyser analyser = new();
    private readonly Employee driver = new()
    {
        Id = Guid.NewGuid(), FirstName = "Anna", LastName = "Driver", EmployeeNumber = "100", DriverProfile = new DriverProfile()
    };
    private readonly DocumentActor actor = new(Guid.NewGuid(), "anna", false);
    private readonly DocumentType points = new()
    {
        Id = Guid.NewGuid(), Code = DocumentTypeCodes.PenaltyPoints, Name = "PP", Kind = DocumentKind.Sign,
        ExpiryRule = DocumentExpiryRule.PenaltyPoints
    };
    private readonly DocumentType obu = new()
    {
        Id = Guid.NewGuid(), Code = DocumentTypeCodes.Obu, Name = "OBU", Kind = DocumentKind.Sign, ExpiryRule = DocumentExpiryRule.None
    };
    private readonly DocumentType passport = new()
    {
        Id = Guid.NewGuid(), Code = DocumentTypeCodes.Passport, Name = "Passport", Kind = DocumentKind.Upload
    };

    public DocumentSigningTests()
    {
        var time = TimeProvider.System;
        service = new DocumentSigningService(db, protector, new DocumentVehicleService(db, time),
            new StoreSettingsService(db, Options.Create(new AbsencePdfOptions { StoreId = "S1", StoreName = "Main" })), analyser, time);
        db.Employees.Add(driver);
        db.DocumentTypes.AddRange(points, obu, passport);
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Driver };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "100", EmployeeId = driver.Id };
        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        db.SaveChanges();
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public void RejectsAPdfWithoutFields()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream);

        Assert.Throws<InvalidDataException>(() => DocumentTemplatePdf.ReadFields(stream.ToArray()));
    }

    [Fact]
    public void AcceptsAFormWhosePagesCanBeDrawnOn()
    {
        Assert.NotEmpty(DocumentTemplatePdf.ReadFields(Template()));
    }

    [Fact]
    public void RejectsAFileThatIsNotAPdf()
    {
        Assert.Throws<InvalidDataException>(() => DocumentTemplatePdf.ReadFields([1, 2, 3]));
    }

    [Fact]
    public void GuessesFieldSourcesFromTheirNames()
    {
        var fields = DocumentTemplatePdf.ReadFields(Template());

        Assert.Equal(DocumentFieldSource.Today, fields.Single(field => field.Name == "notification_date").Source);
        Assert.Equal(DocumentFieldSource.StoreId, fields.Single(field => field.Name == "store_id").Source);
        Assert.Equal(DocumentFieldSource.StoreName, fields.Single(field => field.Name == "store_name").Source);
        Assert.Equal(DocumentFieldSource.ManagerName, fields.Single(field => field.Name == "manager_name").Source);
        Assert.Equal(DocumentFieldSource.ManagerSignature, fields.Single(field => field.Name == "manager_signature").Source);
        Assert.Equal("Reason For Sickness", fields.Single(field => field.Name == "reason_for_sickness").Label);
    }

    [Fact]
    public async Task FormPrefillsWhatIsKnownAndAsksForTheRest()
    {
        await Setup(points, ("manager_name", DocumentFieldSource.FullName), ("store_id", DocumentFieldSource.FirstName),
            ("store_name", DocumentFieldSource.LastName), ("team_member", DocumentFieldSource.Ask));

        var form = await service.GetFormAsync(driver, points.Id, default);

        Assert.Equal("Anna Driver", form!.Fields.Single(field => field.Name == "manager_name").Value);
        Assert.Equal("Anna", form.Fields.Single(field => field.Name == "store_id").Value);
        Assert.Equal("Driver", form.Fields.Single(field => field.Name == "store_name").Value);
        Assert.Null(form.Fields.Single(field => field.Name == "team_member").Value);
        Assert.DoesNotContain(form.Fields, field => field.Name == Signed);
    }

    [Fact]
    public async Task UploadTypeHasNoSignForm()
    {
        await Setup(passport);

        Assert.Null(await service.GetFormAsync(driver, passport.Id, default));
        Assert.Null(await service.SignAsync(driver, actor, passport.Id, Request(), default));
    }

    [Fact]
    public void FormNeedsExactlyOneSignatureFieldToBeReady()
    {
        Assert.True(DocumentSigningService.IsReady("[{\"source\":\"Signature\"},{\"source\":\"Ask\"}]"));
        Assert.False(DocumentSigningService.IsReady("[{\"source\":\"Ask\"}]"));
        Assert.False(DocumentSigningService.IsReady("[{\"source\":\"Signature\"},{\"source\":\"Signature\"}]"));
    }

    [Fact]
    public async Task TypeWithoutATemplateHasNoSignForm()
    {
        Assert.Null(await service.GetFormAsync(driver, points.Id, default));
    }

    [Fact]
    public async Task SigningStoresAFilledEncryptedPdfAndCalculatesTheDueDate()
    {
        await Setup(points, ("team_member", DocumentFieldSource.Ask), ("original_shift", DocumentFieldSource.PenaltyPoints));

        var id = await service.SignAsync(driver, actor, points.Id, Request(("team_member", "Typed value"), ("original_shift", "4")), default);

        var document = await db.EmployeeDocuments.SingleAsync(item => item.Id == id);
        Assert.Equal(4, document.PenaltyPoints);
        Assert.Equal(document.SigningDate!.Value.AddMonths(1), document.ExpiryDate);
        Assert.Equal(DocumentReviewStatus.Clear, document.ReviewStatus);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        Assert.Equal("application/pdf", file.ContentType);
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        using var saved = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var field = Assert.IsType<PdfTextField>(saved.AcroForm!.Fields["team_member"]);
        Assert.Equal("Typed value", field.Text);
        Assert.True(field.ReadOnly);
        Assert.Contains(db.DocumentAuditEvents, item => item.Action == DocumentAuditAction.Signed);
    }

    [Fact]
    public async Task SignatureFieldBoxIsRemovedSoItCannotCoverTheSignature()
    {
        await Setup(points);
        var original = PageAnnotations(Template());

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        Assert.Equal(original - 1, PageAnnotations(pdf));
    }

    private static int PageAnnotations(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfSharp.Pdf.PdfPage>().Sum(page => page.Annotations.Count);
    }

    [Fact]
    public async Task FormWithAManagerPartWaitsForTheManagerThenLocks()
    {
        await Setup(points, ("manager_name", DocumentFieldSource.ManagerName), ("manager_signature", DocumentFieldSource.ManagerSignature));
        var id = (await service.SignAsync(driver, actor, points.Id, Request(), default))!.Value;
        var document = await db.EmployeeDocuments.Include(item => item.DocumentType).SingleAsync(item => item.Id == id);

        Assert.Equal(DocumentRowState.WaitingForManager, DocumentRequirementResolver.StateOf(document, new DateOnly(2026, 11, 6)));
        Assert.Equal(id, Assert.Single(await service.ListSignOffsAsync(default)).DocumentId);
        var form = await service.GetFormAsync(driver, points.Id, default);
        Assert.DoesNotContain(form!.Fields, field => field.Name.StartsWith("manager_"));

        var signed = await service.ManagerSignAsync(id, Manager(), new DocumentSignOffRequest { ManagerName = "Mary Manager", Signature = Signature }, default);

        Assert.True(signed);
        Assert.Empty(await service.ListSignOffsAsync(default));
        Assert.Equal("Mary Manager", (await db.EmployeeDocuments.SingleAsync()).ManagerSignedByName);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        using var saved = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var name = Assert.IsType<PdfTextField>(saved.AcroForm!.Fields["manager_name"]);
        Assert.Equal("Mary Manager", name.Text);
        Assert.True(name.ReadOnly);
        Assert.Contains(db.DocumentAuditEvents, item => item.Action == DocumentAuditAction.ManagerSigned);
    }

    [Fact]
    public async Task ManagerSeesTheFlagsAndSigningClearsTheReview()
    {
        await Setup(points, ("manager_signature", DocumentFieldSource.ManagerSignature));
        analyser.Problems = ["The phone number can't be valid."];
        var id = (await service.SignAsync(driver, actor, points.Id, Request(), default))!.Value;

        var form = await service.GetSignOffAsync(id, Manager(), default);
        await service.ManagerSignAsync(id, Manager(), new DocumentSignOffRequest { Signature = Signature }, default);

        Assert.Equal(["The phone number can't be valid."], form!.ReviewReasons);
        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.Clear, document.ReviewStatus);
        Assert.Empty(document.ReviewReasons);
        Assert.Contains(db.DocumentAuditEvents, item => item.Action == DocumentAuditAction.Reviewed);
    }

    [Fact]
    public async Task ManagerCannotSignTheirOwnForm()
    {
        await Setup(points, ("manager_signature", DocumentFieldSource.ManagerSignature));
        var id = (await service.SignAsync(driver, actor, points.Id, Request(), default))!.Value;
        var own = new DocumentSignOffActor(new DocumentActor(Guid.NewGuid(), "me", false), driver.Id, "Anna Driver");

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.ManagerSignAsync(
            id, own, new DocumentSignOffRequest { Signature = Signature }, default));
    }

    [Fact]
    public async Task SignedFormsAreNoLongerAvailableToManagers()
    {
        await Setup(points, ("manager_signature", DocumentFieldSource.ManagerSignature));
        var id = (await service.SignAsync(driver, actor, points.Id, Request(), default))!.Value;
        Assert.NotNull(await service.ReadSignOffPdfAsync(id, Manager(), default));
        await service.ManagerSignAsync(id, Manager(), new DocumentSignOffRequest { Signature = Signature }, default);

        Assert.Null(await service.ReadSignOffPdfAsync(id, Manager(), default));
        Assert.Null(await service.GetSignOffAsync(id, Manager(), default));
        Assert.False(await service.ManagerSignAsync(id, Manager(), new DocumentSignOffRequest { Signature = Signature }, default));
    }

    [Fact]
    public async Task FormWithoutAManagerPartIsFinishedOnceSigned()
    {
        await Setup(points);

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        Assert.False((await db.EmployeeDocuments.SingleAsync()).AwaitingManagerSignature);
        Assert.Empty(await service.ListSignOffsAsync(default));
    }

    [Fact]
    public async Task OnlyOneManagerSignatureFieldIsAllowed()
    {
        await Setup(points);
        var fields = (await service.GetTemplateAsync(points.Id, default))!.Fields.ToList();
        fields.Single(field => field.Name == "manager_name").Source = DocumentFieldSource.ManagerSignature;
        fields.Single(field => field.Name == "manager_signature").Source = DocumentFieldSource.ManagerSignature;

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SaveFieldsAsync(points.Id, fields, default));
    }

    [Fact]
    public async Task SavedSignatureSignsTheManagerPartAutomatically()
    {
        await Setup(points, ("manager_name", DocumentFieldSource.ManagerName), ("manager_signature", DocumentFieldSource.ManagerSignature));
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { ManagerName = "Mary Manager", Signature = Signature }, default);

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.False(document.AwaitingManagerSignature);
        Assert.Equal("Mary Manager", document.ManagerSignedByName);
        Assert.Empty(await service.ListSignOffsAsync(default));
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        using var saved = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal("Mary Manager", Assert.IsType<PdfTextField>(saved.AcroForm!.Fields["manager_name"]).Text);
        Assert.Contains(db.DocumentAuditEvents, item => item.ActorName == "Mary Manager (automatic)" && item.Action == DocumentAuditAction.ManagerSigned);
    }

    [Fact]
    public async Task SavedSignatureNeverSignsThatManagersOwnForm()
    {
        await Setup(points, ("manager_signature", DocumentFieldSource.ManagerSignature));
        var own = new DocumentSignOffActor(new DocumentActor(Guid.NewGuid(), "me", false), driver.Id, "Anna Driver");
        await service.SaveAutomaticSignatureAsync(own, new DocumentSignOffRequest { Signature = Signature }, default);

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        Assert.True((await db.EmployeeDocuments.SingleAsync()).AwaitingManagerSignature);
    }

    [Fact]
    public async Task RemovingTheSavedSignatureSendsFormsBackToTheList()
    {
        await Setup(points, ("manager_signature", DocumentFieldSource.ManagerSignature));
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { Signature = Signature }, default);

        Assert.True(await service.RemoveAutomaticSignatureAsync(default));
        await service.SignAsync(driver, actor, points.Id, Request(), default);

        Assert.Single(await service.ListSignOffsAsync(default));
        Assert.Empty(db.DocumentManagerSignatures);
    }

    [Fact]
    public void OnlyAdministratorsManageTheAutomaticSignature()
    {
        var methods = typeof(Roaster_Generator.Controllers.AdminDocumentSignOffsController).GetMethods()
            .Where(method => method.Name.EndsWith("AutomaticSignature", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, methods.Length);
        Assert.All(methods, method => Assert.Contains(
            method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(),
            attribute => attribute.Policy == AuthorizationPolicies.Admin));
    }

    [Fact]
    public async Task SavedSignatureIsStoredEncrypted()
    {
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { Signature = Signature }, default);

        var saved = await db.DocumentManagerSignatures.SingleAsync();
        Assert.NotEqual(Convert.FromBase64String(Signature), saved.EncryptedContent);
    }

    [Fact]
    public async Task FranchiseeComesFromStoreSettingsAndCannotBeChanged()
    {
        await AddObuTemplate("{\"name\":\"store_id\",\"label\":\"Franchisee\",\"required\":true,\"source\":\"FranchiseeName\"}");
        await new StoreSettingsService(db, Options.Create(new AbsencePdfOptions()))
            .SaveAsync(new Contracts.Settings.StoreSettingsRequest { StoreId = "S", StoreName = "N", FranchiseeName = " Acme Ltd " }, default);

        var franchisee = Assert.Single((await service.GetFormAsync(driver, obu.Id, default))!.Fields);
        Assert.Equal("Acme Ltd", franchisee.Value);
        Assert.True(franchisee.ReadOnly);

        await service.SignAsync(driver, actor, obu.Id, Request(("store_id", "Someone Else")), default);

        Assert.Equal("Acme Ltd", await SignedText("store_id"));
    }

    [Theory]
    [InlineData(DriverType.Moped, "true")]
    [InlineData(DriverType.Car, "")]
    public async Task CoverBoxesFollowTheVehicleAndAreNotAsked(DriverType type, string expected)
    {
        (await db.DriverProfiles.SingleAsync()).DriverType = type;
        await db.SaveChangesAsync();
        await AddObuTemplate(
            "{\"name\":\"store_id\",\"label\":\"OBU\",\"source\":\"AlwaysTicked\"}",
            "{\"name\":\"store_name\",\"label\":\"Moped\",\"source\":\"TickedForMoped\"}");

        Assert.Empty((await service.GetFormAsync(driver, obu.Id, default))!.Fields);
        await service.SignAsync(driver, actor, obu.Id, Request(), default);

        Assert.Equal("true", await SignedText("store_id"));
        Assert.Equal(expected, await SignedText("store_name"));
    }

    private async Task AddObuTemplate(params string[] fields)
    {
        var signature = "{\"name\":\"reason_for_sickness\",\"label\":\"s\",\"source\":\"Signature\"}";
        db.DocumentTemplates.Add(new DocumentTemplate
        {
            DocumentTypeId = obu.Id,
            Content = Template(),
            FileName = "obu.pdf",
            Fields = $"[{string.Join(',', fields.Append(signature))}]"
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> SignedText(string field)
    {
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        using var saved = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return Assert.IsType<PdfTextField>(saved.AcroForm!.Fields[field]).Text;
    }

    [Fact]
    public async Task FranchiseeCanBeTypedUntilItIsSetUp()
    {
        await AddObuTemplate("{\"name\":\"store_id\",\"label\":\"Franchisee\",\"required\":true,\"source\":\"FranchiseeName\"}");

        var form = await service.GetFormAsync(driver, obu.Id, default);

        Assert.False(Assert.Single(form!.Fields).ReadOnly);
    }

    private const string HealthFields =
        "{\"name\":\"health_skin_yes\",\"label\":\"Skin: Yes\",\"isCheckbox\":true,\"required\":false,\"source\":\"Ask\"}," +
        "{\"name\":\"health_skin_no\",\"label\":\"Skin: No\",\"isCheckbox\":true,\"required\":false,\"source\":\"Ask\"}";

    [Fact]
    public async Task HealthQuestionsMustAllBeAnswered()
    {
        await AddObuTemplate(HealthFields);

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SignAsync(driver, actor, obu.Id, Request(), default));
        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SignAsync(driver, actor, obu.Id,
            Request(("health_skin_yes", "true"), ("health_skin_no", "true")), default));
    }

    [Fact]
    public async Task AnsweringNoToHealthQuestionsNeedsNoReview()
    {
        await AddObuTemplate(HealthFields);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        Assert.Equal(DocumentReviewStatus.Clear, (await db.EmployeeDocuments.SingleAsync()).ReviewStatus);
    }

    [Fact]
    public async Task AnsweringYesFlagsTheFormWithoutRepeatingHealthDetails()
    {
        await AddObuTemplate(HealthFields);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_yes", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.NeedsReview, document.ReviewStatus);
        Assert.DoesNotContain("Skin", Assert.Single(document.ReviewReasons));
    }

    [Fact]
    public async Task ProblemsFoundAfterSigningFlagTheFormForReview()
    {
        await AddObuTemplate(HealthFields);
        analyser.Problems = ["The phone number can't be valid."];

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.NeedsReview, document.ReviewStatus);
        Assert.Equal(["The phone number can't be valid."], document.ReviewReasons);
    }

    [Fact]
    public async Task ACleanCheckLeavesTheFormClear()
    {
        await AddObuTemplate(HealthFields);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.Clear, document.ReviewStatus);
        Assert.Empty(document.ReviewReasons);
        Assert.Equal(1, analyser.Calls);
        Assert.Equal("OBU", analyser.LastFormName);
        Assert.True(analyser.LastPdf?.Length > 0);
        Assert.Contains(analyser.LastAnswers, answer => answer.EndsWith(": ticked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFormThatCouldNotBeCheckedIsFlagged()
    {
        await AddObuTemplate(HealthFields);
        analyser.Problems = null;

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(DocumentReviewStatus.NeedsReview, document.ReviewStatus);
        Assert.Contains("couldn't be checked", Assert.Single(document.ReviewReasons));
    }

    [Fact]
    public async Task AFlaggedFormIsNotSignedAutomatically()
    {
        await AddObuTemplate(HealthFields, ManagerSignatureField);
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { ManagerName = "Mary", Signature = Signature }, default);
        analyser.Problems = ["The answers contradict each other."];

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.True(document.AwaitingManagerSignature);
        Assert.Null(document.ManagerSignedByName);
    }

    [Fact]
    public async Task TheFormIsCheckedBeforeTheManagerPartIsAdded()
    {
        await AddObuTemplate(HealthFields, ManagerSignatureField);
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { ManagerName = "Mary", Signature = Signature }, default);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        Assert.NotEqual(pdf, analyser.LastPdf);
    }

    [Fact]
    public void FormCheckOutputIsLimitedAndRejectedWhenUnusable()
    {
        var problems = string.Join(",", Enumerable.Repeat($"\"{new string('x', 300)}\"", 9));

        var parsed = DocumentAnalysisParser.ParseFormCheck($$"""{"problems":[{{problems}}]}""")!;

        Assert.Equal(5, parsed.Count);
        Assert.All(parsed, problem => Assert.Equal(200, problem.Length));
        Assert.Empty(DocumentAnalysisParser.ParseFormCheck("""{"problems":[]}""")!);
        Assert.Null(DocumentAnalysisParser.ParseFormCheck("""{"other":1}"""));
        Assert.Null(DocumentAnalysisParser.ParseFormCheck("not json"));
    }

    [Fact]
    public void FormCheckRequestListsTheAnswers()
    {
        var request = ClaudeDocumentAnalyser.FormCheckRequest("Food handler", "John Smith", ["Phone: 123"]);

        Assert.Contains("Food handler", request);
        Assert.Contains("John Smith", request);
        Assert.Contains("- Phone: 123", request);
    }

    private const string ManagerSignatureField = "{\"name\":\"manager_signature\",\"label\":\"m\",\"source\":\"ManagerSignature\"}";

    [Fact]
    public async Task HealthFormWithAllNoIsSignedAutomatically()
    {
        await AddObuTemplate(HealthFields, ManagerSignatureField);
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { ManagerName = "Mary", Signature = Signature }, default);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_no", "true")), default);

        Assert.False((await db.EmployeeDocuments.SingleAsync()).AwaitingManagerSignature);
    }

    [Fact]
    public async Task HealthFormWithAYesWaitsForAManagerEvenWithASavedSignature()
    {
        await AddObuTemplate(HealthFields, ManagerSignatureField);
        await service.SaveAutomaticSignatureAsync(Manager(), new DocumentSignOffRequest { ManagerName = "Mary", Signature = Signature }, default);

        await service.SignAsync(driver, actor, obu.Id, Request(("health_skin_yes", "true")), default);

        var document = await db.EmployeeDocuments.SingleAsync();
        Assert.True(document.AwaitingManagerSignature);
        Assert.Null(document.ManagerSignedByName);
        Assert.Single(await service.ListSignOffsAsync(default));
    }

    private static DocumentSignOffActor Manager() =>
        new(new DocumentActor(Guid.NewGuid(), "mary", false), Guid.NewGuid(), "Mary Manager");

    [Fact]
    public async Task TheDateCannotBeBackdated()
    {
        await Setup(points, ("notification_date", DocumentFieldSource.Today));

        await service.SignAsync(driver, actor, points.Id, Request(("notification_date", "01/01/2001")), default);

        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var pdf = protector.Unprotect(file.Id, new(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        using var saved = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.NotEqual("01/01/2001", Assert.IsType<PdfTextField>(saved.AcroForm!.Fields["notification_date"]).Text);
    }

    [Fact]
    public async Task MissingRequiredValueIsRefused()
    {
        await Setup(points, ("team_member", DocumentFieldSource.Ask));

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SignAsync(driver, actor, points.Id, Request(), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAEC")]
    public async Task InvalidSignatureIsRefused(string signature)
    {
        await Setup(points);

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SignAsync(
            driver, actor, points.Id, new DocumentSignRequest { Signature = signature }, default));
    }

    [Fact]
    public async Task InvalidPenaltyPointsAreRefused()
    {
        await Setup(points, ("original_shift", DocumentFieldSource.PenaltyPoints));

        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => service.SignAsync(driver, actor, points.Id, Request(("original_shift", "many")), default));
    }

    [Fact]
    public async Task SigningAgainReplacesTheOldDocument()
    {
        await Setup(points);
        await service.SignAsync(driver, actor, points.Id, Request(), default);

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        Assert.Single(db.EmployeeDocuments);
        Assert.Single(db.EmployeeDocumentFiles);
    }

    [Fact]
    public async Task SigningClearsARequestForANewSignature()
    {
        await Setup(points);
        await service.SignAsync(driver, actor, points.Id, Request(), default);
        var signed = await db.EmployeeDocuments.SingleAsync();
        signed.NewSignatureRequested = true;
        await db.SaveChangesAsync();

        await service.SignAsync(driver, actor, points.Id, Request(), default);

        Assert.False((await db.EmployeeDocuments.SingleAsync()).NewSignatureRequested);
    }

    [Fact]
    public async Task BusinessUseInsuranceTakesTheCurrentRegistration()
    {
        db.EmployeeVehicles.Add(new EmployeeVehicle { EmployeeId = driver.Id, Registration = "241-D-111" });
        await db.SaveChangesAsync();
        await Setup(obu);

        await service.SignAsync(driver, actor, obu.Id, Request(), default);

        Assert.Equal("241-D-111", (await db.EmployeeDocuments.SingleAsync()).VehicleRegistration);
    }

    [Fact]
    public async Task FieldsMustMatchTheForm()
    {
        await Setup(points);
        var fields = (await service.GetTemplateAsync(points.Id, default))!.Fields.Skip(1).ToList();

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SaveFieldsAsync(points.Id, fields, default));
    }

    [Fact]
    public async Task ExactlyOneSignatureFieldIsNeeded()
    {
        await Setup(points);
        var fields = (await service.GetTemplateAsync(points.Id, default))!.Fields.ToList();
        foreach (var field in fields) field.Source = DocumentFieldSource.Ignore;

        await Assert.ThrowsAsync<DocumentSettingsException>(() => service.SaveFieldsAsync(points.Id, fields, default));
    }

    [Fact]
    public void AnsweringStateNeedsNewSignature()
    {
        var document = new EmployeeDocument
        {
            DocumentType = new DocumentType { ExpiryRule = DocumentExpiryRule.None }, NewSignatureRequested = true
        };

        Assert.Equal(DocumentRowState.NeedsNewSignature, DocumentRequirementResolver.StateOf(document, new DateOnly(2026, 11, 6)));
    }

    private const string Signed = "reason_for_sickness";

    private sealed class FakeAnalyser : IDocumentAnalyser
    {
        public IReadOnlyList<string>? Problems { get; set; } = [];
        public int Calls { get; private set; }
        public string? LastFormName { get; private set; }
        public byte[]? LastPdf { get; private set; }
        public IReadOnlyList<string> LastAnswers { get; private set; } = [];
        public bool IsConfigured => true;

        public Task<DocumentAnalysis?> AnalyseAsync(
            IReadOnlyList<ValidatedDocumentFile> files, IReadOnlyList<DocumentCatalogueEntry> catalogue, CancellationToken cancellationToken) =>
            Task.FromResult<DocumentAnalysis?>(null);

        public Task<IReadOnlyList<string>?> CheckSignedFormAsync(
            byte[] pdf, string formName, string employeeName, IReadOnlyList<string> answers, CancellationToken cancellationToken)
        {
            Calls++;
            LastFormName = formName;
            LastPdf = pdf;
            LastAnswers = answers;
            return Task.FromResult(Problems);
        }
    }

    private static byte[] Template()
    {
        using var stream = typeof(AbsentPdfService).Assembly
            .GetManifestResourceStream("Roaster_Generator.PdfTemplates.absence-notification-roi-fillable.pdf")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Uses the absence form as a fillable PDF, with one text field standing in for the signature.</summary>
    private async Task Setup(DocumentType type, params (string Name, DocumentFieldSource Source)[] sources)
    {
        await service.SaveTemplateAsync(type.Id, Template(), "form.pdf", default);
        var fields = (await service.GetTemplateAsync(type.Id, default))!.Fields.ToList();
        foreach (var field in fields)
        {
            field.Source = field.Name == Signed ? DocumentFieldSource.Signature : DocumentFieldSource.Ignore;
            foreach (var source in sources.Where(item => item.Name == field.Name)) field.Source = source.Source;
        }

        await service.SaveFieldsAsync(type.Id, fields, default);
    }

    private static DocumentSignRequest Request(params (string Name, string Value)[] values) => new()
    {
        Signature = Signature,
        Values = values.ToDictionary(item => item.Name, item => (string?)item.Value)
    };
}
