using System.Security.Cryptography;
using System.Text;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class DocumentRequirementResolverTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Theory]
    [InlineData(DriverType.Car, true, DocumentCategory.CarOwn)]
    [InlineData(DriverType.Car, false, DocumentCategory.CarCompany)]
    [InlineData(DriverType.Moped, true, DocumentCategory.MopedOwn)]
    [InlineData(DriverType.Moped, false, DocumentCategory.MopedCompany)]
    [InlineData(DriverType.EBike, true, DocumentCategory.EBikeOwn)]
    [InlineData(DriverType.EBike, false, DocumentCategory.EBikeCompany)]
    public void MapsEachDriverProfileToItsOwnList(DriverType type, bool isOwn, DocumentCategory expected)
    {
        var employee = new Employee { DriverProfile = new DriverProfile { DriverType = type, IsOwn = isOwn } };

        Assert.Equal([expected], DocumentRequirementResolver.CategoriesFor(employee, [RoleNames.Driver]));
    }

    [Fact]
    public void MultipleRolesUseTheUnionOfTheirLists()
    {
        var employee = new Employee
        {
            DriverProfile = new DriverProfile { IsOwn = false },
            InStoreProfile = new InStoreProfile(),
            ManagerProfile = new ManagerProfile()
        };

        var categories = DocumentRequirementResolver.CategoriesFor(
            employee, [RoleNames.Driver, RoleNames.InStore, RoleNames.Manager, RoleNames.Admin]);

        Assert.Equal(
            [DocumentCategory.CarCompany, DocumentCategory.InStore, DocumentCategory.Manager],
            categories.OrderBy(category => category));
    }

    [Fact]
    public void ProfileWithoutItsRoleDoesNotCount()
    {
        var employee = new Employee { DriverProfile = new DriverProfile() };

        Assert.Empty(DocumentRequirementResolver.CategoriesFor(employee, [RoleNames.InStore]));
    }

    [Fact]
    public void MissingDocumentShowsNotUploaded()
    {
        var row = Assert.Single(Resolve([Requirement(Types.Spf)], []));

        Assert.Equal(DocumentRowState.NotUploaded, row.State);
        Assert.True(row.IsRequired);
        Assert.Null(row.Document);
    }

    [Fact]
    public void OnlyTheEmployeesCategoriesApply()
    {
        var rows = Resolve([Requirement(Types.Spf, category: DocumentCategory.InStore)], []);

        Assert.Empty(rows);
    }

    [Fact]
    public void InactiveTypeIsNotRequired()
    {
        var retired = new DocumentType { Id = Guid.NewGuid(), Code = "retired", Name = "Retired", IsActive = false };

        Assert.Empty(Resolve([Requirement(retired)], []));
    }

    [Fact]
    public void NonEuItemDependsOnPassportUntilOneExists()
    {
        var row = Assert.Single(Resolve([Requirement(Types.Irp, DocumentRequirementCondition.NonEuPassport)], []));

        Assert.Equal(DocumentRowState.DependsOnPassport, row.State);
    }

    [Theory]
    [InlineData("IE")]
    [InlineData("NO")]
    [InlineData("CH")]
    [InlineData("GB")]
    public void ExemptPassportDropsTheNonEuItem(string country)
    {
        var rows = Resolve(
            [Requirement(Types.Irp, DocumentRequirementCondition.NonEuPassport)],
            [Passport(country)]);

        Assert.DoesNotContain(rows, row => row.DocumentType.Code == DocumentTypeCodes.Irp);
    }

    [Fact]
    public void NonEuPassportRequiresIrp()
    {
        var rows = Resolve(
            [Requirement(Types.Irp, DocumentRequirementCondition.NonEuPassport)],
            [Passport("IN")]);

        Assert.Equal(DocumentRowState.NotUploaded, rows.Single(row => row.DocumentType == Types.Irp).State);
    }

    [Fact]
    public void AlwaysInOneListBeatsConditionInAnother()
    {
        var rows = Resolve(
            [
                Requirement(Types.Irp, DocumentRequirementCondition.NonEuPassport),
                Requirement(Types.Irp, category: DocumentCategory.InStore)
            ],
            [Passport("IE")],
            [DocumentCategory.CarOwn, DocumentCategory.InStore]);

        Assert.Equal(DocumentRowState.NotUploaded, rows.Single(row => row.DocumentType == Types.Irp).State);
    }

    [Fact]
    public void OutstandingIrpMentionsTheLabourMarketRoute()
    {
        var missing = NonEuRows().Single(row => row.DocumentType == Types.Irp);
        var onFile = NonEuRows(Document(Types.Irp, Today.AddYears(1))).Single(row => row.DocumentType == Types.Irp);

        Assert.EndsWith("or Labour market permission", DocumentRequirementResolver.DisplayName(missing));
        Assert.Equal(Types.Irp.Name, DocumentRequirementResolver.DisplayName(onFile));
    }

    [Fact]
    public void LmpAloneDoesNotSatisfyIrpAndAsksForTrc()
    {
        var rows = NonEuRows(Document(Types.Lmp, Today.AddYears(1)));

        var irpRow = rows.Single(row => row.DocumentType == Types.Irp);
        Assert.Equal(DocumentRowState.NotUploaded, irpRow.State);
        Assert.Equal(Types.Lmp, irpRow.Document!.DocumentType);
        Assert.Equal(DocumentRowState.NotUploaded, rows.Single(row => row.DocumentType == Types.Trc).State);
    }

    [Fact]
    public void TrcAloneDoesNotSatisfyIrp()
    {
        var rows = NonEuRows(Document(Types.Trc, Today.AddYears(1)));

        Assert.Equal(DocumentRowState.NotUploaded, rows.Single(row => row.DocumentType == Types.Irp).State);
        Assert.False(rows.Single(row => row.DocumentType == Types.Trc).IsRequired);
    }

    [Fact]
    public void LmpWithTrcSatisfiesIrp()
    {
        var rows = NonEuRows(Document(Types.Lmp, Today.AddYears(1)), Document(Types.Trc, Today.AddYears(1)));

        Assert.Equal(DocumentRowState.Valid, rows.Single(row => row.DocumentType == Types.Irp).State);
        Assert.True(rows.Single(row => row.DocumentType == Types.Trc).IsRequired);
        Assert.DoesNotContain(rows, row => row.DocumentType == Types.Lmp);
    }

    [Fact]
    public void ExpiredTrcFailsTheLmpRoute()
    {
        var rows = NonEuRows(Document(Types.Lmp, Today.AddYears(1)), Document(Types.Trc, Today.AddDays(-1)));

        Assert.Equal(DocumentRowState.Expired, rows.Single(row => row.DocumentType == Types.Irp).State);
    }

    [Fact]
    public void ValidLmpRouteBeatsExpiredIrpWhichStaysVisible()
    {
        var rows = NonEuRows(
            Document(Types.Irp, Today.AddDays(-1)),
            Document(Types.Lmp, Today.AddYears(1)),
            Document(Types.Trc, Today.AddYears(1)));

        var required = rows.Single(row => row.DocumentType == Types.Irp && row.IsRequired);
        Assert.Equal(DocumentRowState.Valid, required.State);
        var superseded = rows.Single(row => row.DocumentType == Types.Irp && !row.IsRequired);
        Assert.Equal(DocumentRowState.Expired, superseded.State);
    }

    [Fact]
    public void ExpiredIrpIsNotHiddenByLmpWithoutTrc()
    {
        var rows = NonEuRows(Document(Types.Irp, Today.AddDays(-1)), Document(Types.Lmp, Today.AddYears(1)));

        var row = rows.Single(item => item.DocumentType == Types.Irp);
        Assert.Equal(DocumentRowState.Expired, row.State);
        Assert.Equal(Types.Irp, row.Document!.DocumentType);
    }

    [Theory]
    [InlineData("2", DocumentRowState.NotUploaded)]
    [InlineData("Stamp 2", DocumentRowState.NotUploaded)]
    [InlineData("stamp2", DocumentRowState.NotUploaded)]
    [InlineData("2A", DocumentRowState.NeedsAdminDecision)]
    [InlineData("Stamp 2A", DocumentRowState.NeedsAdminDecision)]
    [InlineData(null, DocumentRowState.NeedsAdminDecision)]
    public void CollegeLetterFollowsTheIrpStamp(string? stamp, DocumentRowState expected)
    {
        var irp = Document(Types.Irp, Today.AddYears(1));
        irp.Stamp = stamp;

        var rows = Resolve([Requirement(Types.CollegeLetter, DocumentRequirementCondition.Stamp2)], [irp]);

        Assert.Equal(expected, rows.Single(row => row.DocumentType == Types.CollegeLetter).State);
    }

    [Theory]
    [InlineData("Stamp 4")]
    [InlineData("1G")]
    public void OtherStampsNeedNoCollegeLetter(string stamp)
    {
        var irp = Document(Types.Irp, Today.AddYears(1));
        irp.Stamp = stamp;

        var rows = Resolve([Requirement(Types.CollegeLetter, DocumentRequirementCondition.Stamp2)], [irp]);

        Assert.DoesNotContain(rows, row => row.DocumentType == Types.CollegeLetter);
    }

    [Fact]
    public void NoIrpNeedsNoCollegeLetter()
    {
        Assert.Empty(Resolve([Requirement(Types.CollegeLetter, DocumentRequirementCondition.Stamp2)], []));
    }

    [Theory]
    [InlineData(-1, DocumentRowState.Expired)]
    [InlineData(0, DocumentRowState.ExpiringSoon)]
    [InlineData(30, DocumentRowState.ExpiringSoon)]
    [InlineData(31, DocumentRowState.Valid)]
    public void ExpiryBoundaries(int days, DocumentRowState expected)
    {
        var document = Document(Types.Passport, Today.AddDays(days));

        Assert.Equal(expected, DocumentRequirementResolver.StateOf(document, Today));
    }

    [Fact]
    public void ExpiryDateIsIgnoredWhenTheTypeHasNoExpiry()
    {
        var document = Document(Types.Spf, Today.AddDays(-1));

        Assert.Equal(DocumentRowState.Valid, DocumentRequirementResolver.StateOf(document, Today));
    }

    [Fact]
    public void ReviewFlagShowsAsWaitingButExpiredWins()
    {
        var waiting = Document(Types.Passport, Today.AddYears(1));
        waiting.ReviewStatus = DocumentReviewStatus.NeedsReview;
        var expired = Document(Types.Passport, Today.AddDays(-1));
        expired.ReviewStatus = DocumentReviewStatus.NeedsReview;

        Assert.Equal(DocumentRowState.WaitingForReview, DocumentRequirementResolver.StateOf(waiting, Today));
        Assert.Equal(DocumentRowState.Expired, DocumentRequirementResolver.StateOf(expired, Today));
    }

    [Fact]
    public void DocumentOutsideTheListIsShownAsOptional()
    {
        var row = Assert.Single(Resolve([], [Document(Types.Spf, null)]));

        Assert.False(row.IsRequired);
        Assert.Equal(Types.Spf, row.DocumentType);
    }

    [Fact]
    public void RowsFollowCatalogueOrder()
    {
        var rows = Resolve([Requirement(Types.Spf), Requirement(Types.Passport)], []);

        Assert.Equal([Types.Passport, Types.Spf], rows.Select(row => row.DocumentType));
    }

    private static IReadOnlyList<DocumentChecklistRow> NonEuRows(params EmployeeDocument[] documents) => Resolve(
        [Requirement(Types.Irp, DocumentRequirementCondition.NonEuPassport)],
        [Passport("IN"), .. documents]);

    private static IReadOnlyList<DocumentChecklistRow> Resolve(
        IEnumerable<DocumentRequirement> requirements,
        IEnumerable<EmployeeDocument> documents,
        IEnumerable<DocumentCategory>? categories = null) =>
        DocumentRequirementResolver.Resolve(
            categories ?? [DocumentCategory.CarOwn], requirements, Types.All, documents, Today);

    private static DocumentRequirement Requirement(
        DocumentType type,
        DocumentRequirementCondition condition = DocumentRequirementCondition.Always,
        DocumentCategory category = DocumentCategory.CarOwn) => new()
    {
        Id = Guid.NewGuid(),
        Category = category,
        DocumentType = type,
        DocumentTypeId = type.Id,
        Condition = condition
    };

    private static EmployeeDocument Passport(string country)
    {
        var document = Document(Types.Passport, Today.AddYears(5));
        document.IssuingCountry = country;
        return document;
    }

    private static EmployeeDocument Document(DocumentType type, DateOnly? expiry) => new()
    {
        Id = Guid.NewGuid(),
        DocumentType = type,
        DocumentTypeId = type.Id,
        ExpiryDate = expiry
    };

    private static class Types
    {
        public static readonly DocumentType Passport = Type(DocumentTypeCodes.Passport, 10);
        public static readonly DocumentType Irp = Type(DocumentTypeCodes.Irp, 20);
        public static readonly DocumentType Lmp = Type(DocumentTypeCodes.LabourMarketPermission, 30);
        public static readonly DocumentType Trc = Type(DocumentTypeCodes.TemporaryResidenceCertificate, 40);
        public static readonly DocumentType CollegeLetter = Type(DocumentTypeCodes.CollegeLetter, 50, DocumentExpiryRule.None);
        public static readonly DocumentType Spf = Type(DocumentTypeCodes.Spf, 60, DocumentExpiryRule.None);
        public static readonly DocumentType[] All = [Passport, Irp, Lmp, Trc, CollegeLetter, Spf];

        private static DocumentType Type(string code, int order, DocumentExpiryRule rule = DocumentExpiryRule.Printed) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            ExpiryRule = rule,
            SortOrder = order
        };
    }
}

public sealed class DocumentChecksTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Theory]
    [InlineData(0, "2026-04-15")]
    [InlineData(3, "2026-04-15")]
    [InlineData(4, "2026-02-15")]
    public void PenaltyPointsDueDateFollowsThePointBands(int points, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected),
            DocumentChecks.PenaltyPointsDueDate(new DateOnly(2026, 1, 15), points));
    }

    [Fact]
    public void PenaltyPointsDueDateClampsToMonthEnd()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), DocumentChecks.PenaltyPointsDueDate(new DateOnly(2026, 1, 31), 4));
    }

    [Fact]
    public void PenaltyPointsDueDateNeedsBothValues()
    {
        Assert.Null(DocumentChecks.PenaltyPointsDueDate(null, 1));
        Assert.Null(DocumentChecks.PenaltyPointsDueDate(new DateOnly(2026, 1, 15), null));
        Assert.Null(DocumentChecks.PenaltyPointsDueDate(new DateOnly(2026, 1, 15), -1));
    }

    [Theory]
    [InlineData("JOHN MICHAEL SMITH", "John", "Smith", true)]
    [InlineData("SMITH, JOHN", "John", "Smith", true)]
    [InlineData("Seán Ó Briain", "Sean", "O Briain", true)]
    [InlineData("MARY O'BRIEN", "Mary", "O’Brien", true)]
    [InlineData("ANNA NOWAK-KOWALSKA", "Anna", "Nowak Kowalska", true)]
    [InlineData("JOHN SMYTH", "John", "Smith", false)]
    [InlineData("JOHN", "John", "Smith", false)]
    public void ComparesNamesIgnoringOrderAccentsAndPunctuation(string holder, string first, string last, bool expected)
    {
        Assert.Equal(expected, DocumentChecks.NameMatches(holder, first, last));
    }

    [Fact]
    public void BlankEmployeeNameNeverMatches()
    {
        Assert.False(DocumentChecks.NameMatches("JOHN SMITH", " ", " "));
    }

    [Fact]
    public void CleanDocumentHasNoWarnings()
    {
        Assert.Empty(Evaluate(Facts() with { PrintedExpiryDate = Today.AddDays(1), HolderName = "John Smith" }));
    }

    [Fact]
    public void DocumentExpiringTodayIsNotExpired()
    {
        Assert.Empty(Evaluate(Facts() with { PrintedExpiryDate = Today }));
    }

    [Fact]
    public void FlagsExpiredDocument()
    {
        Assert.Equal([DocumentWarningCode.Expired], Codes(Facts() with { PrintedExpiryDate = Today.AddDays(-1) }));
    }

    [Fact]
    public void FlagsMissingPrintedExpiry()
    {
        Assert.Equal([DocumentWarningCode.MissingExpiry], Codes(Facts()));
    }

    [Fact]
    public void NoExpiryRuleNeverWarnsAboutExpiry()
    {
        var facts = Facts() with { ExpiryRule = DocumentExpiryRule.None, PrintedExpiryDate = Today.AddYears(-1) };

        Assert.Empty(Evaluate(facts));
    }

    [Fact]
    public void PenaltyPointsExpiryComesFromTheCalculation()
    {
        var facts = Facts() with
        {
            ExpiryRule = DocumentExpiryRule.PenaltyPoints,
            PrintedExpiryDate = Today.AddYears(1),
            SigningDate = Today.AddMonths(-2),
            PenaltyPoints = 4
        };

        Assert.Equal([DocumentWarningCode.Expired], Codes(facts));
    }

    [Fact]
    public void FlagsNameMismatch()
    {
        var facts = Facts() with { PrintedExpiryDate = Today.AddDays(1), HolderName = "Jane Doe" };

        Assert.Equal([DocumentWarningCode.NameMismatch], Codes(facts));
    }

    [Fact]
    public void InsuranceMustBeInTheEmployeesName()
    {
        var facts = Facts() with
        {
            DocumentTypeCode = DocumentTypeCodes.Insurance,
            PrintedExpiryDate = Today.AddDays(1),
            HolderName = "Jane Doe"
        };

        Assert.Equal([DocumentWarningCode.NameMismatch], Codes(facts));
    }

    [Fact]
    public void DiscWithNoNameIsNotAMismatch()
    {
        var facts = Facts() with
        {
            DocumentTypeCode = DocumentTypeCodes.MotorTax,
            PrintedExpiryDate = Today.AddDays(1),
            HolderName = null
        };

        Assert.Empty(Evaluate(facts));
    }

    [Fact]
    public void InsuranceStillHasToBeInDate()
    {
        var facts = Facts() with { DocumentTypeCode = DocumentTypeCodes.Insurance, PrintedExpiryDate = Today.AddDays(-1) };

        Assert.Equal([DocumentWarningCode.Expired], Codes(facts));
    }

    [Fact]
    public void AnalysisAcceptsDiscsAndAnyInsurance()
    {
        var prompt = ClaudeDocumentAnalyser.SystemPrompt([]);

        Assert.Contains("nct, motor-tax and insurance can each be the full document or just the disc", prompt);
        Assert.Contains("insurance can be any type of cover.", prompt);
        Assert.Contains("use the last day of that month", prompt);
    }

    [Theory]
    [InlineData("IN", true)]
    [InlineData("GB", true)]
    [InlineData("IE", false)]
    public void LicenceMustBeEuIssued(string country, bool flagged)
    {
        var facts = Facts() with
        {
            DocumentTypeCode = DocumentTypeCodes.DrivingLicence,
            PrintedExpiryDate = Today.AddDays(1),
            IssuingCountry = country
        };

        Assert.Equal(flagged, Codes(facts).Contains(DocumentWarningCode.LicenceNotEuIssued));
    }

    [Fact]
    public void RegistrationComparisonIgnoresCaseSpacesAndHyphens()
    {
        var facts = Facts() with
        {
            DocumentTypeCode = DocumentTypeCodes.Nct,
            PrintedExpiryDate = Today.AddDays(1),
            VehicleRegistration = "241-d-12345"
        };

        Assert.Empty(Evaluate(facts, ["241 D 12345"]));
    }

    [Fact]
    public void FlagsRegistrationMismatchAgainstDocumentsOnFile()
    {
        var facts = Facts() with
        {
            DocumentTypeCode = DocumentTypeCodes.Nct,
            PrintedExpiryDate = Today.AddDays(1),
            VehicleRegistration = "241-D-12345"
        };

        Assert.Equal(
            [DocumentWarningCode.RegistrationMismatch],
            Evaluate(facts, ["241-D-99999"]).Select(warning => warning.Code));
    }

    [Fact]
    public void RegistrationIsOnlyComparedOnVehicleDocuments()
    {
        var facts = Facts() with { PrintedExpiryDate = Today.AddDays(1), VehicleRegistration = "241-D-12345" };

        Assert.Empty(Evaluate(facts, ["241-D-99999"]));
    }

    private static DocumentWarningCode[] Codes(DocumentFacts facts) =>
        Evaluate(facts).Select(warning => warning.Code).ToArray();

    private static IReadOnlyList<DocumentWarning> Evaluate(DocumentFacts facts, IEnumerable<string>? registrations = null) =>
        DocumentChecks.Evaluate(facts, "John", "Smith", registrations ?? [], Today);

    private static DocumentFacts Facts() => new(
        DocumentTypeCodes.Passport, "Passport", DocumentExpiryRule.Printed, null, null, null, null, null, null);
}

public sealed class DocumentFileProtectorTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("passport scan");

    [Fact]
    public void RoundTripsContent()
    {
        var protector = Protector();
        var id = Guid.NewGuid();

        var encrypted = protector.Protect(id, Content);

        Assert.NotEqual(Content, encrypted.Ciphertext);
        Assert.Equal("v1", encrypted.KeyId);
        Assert.Equal(Content, protector.Unprotect(id, encrypted));
    }

    [Fact]
    public void EachEncryptionUsesAFreshNonce()
    {
        var protector = Protector();
        var id = Guid.NewGuid();

        Assert.NotEqual(protector.Protect(id, Content).Nonce, protector.Protect(id, Content).Nonce);
    }

    [Fact]
    public void RejectsTamperedContent()
    {
        var protector = Protector();
        var id = Guid.NewGuid();
        var encrypted = protector.Protect(id, Content);
        encrypted.Ciphertext[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(id, encrypted));
    }

    [Fact]
    public void RejectsCiphertextMovedToAnotherFile()
    {
        var protector = Protector();
        var encrypted = protector.Protect(Guid.NewGuid(), Content);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Guid.NewGuid(), encrypted));
    }

    [Fact]
    public void RejectsWrongKey()
    {
        var id = Guid.NewGuid();
        var encrypted = Protector().Protect(id, Content);

        Assert.ThrowsAny<CryptographicException>(() => Protector().Unprotect(id, encrypted));
    }

    [Fact]
    public void RejectsUnknownKeyId()
    {
        var protector = Protector();
        var id = Guid.NewGuid();
        var encrypted = protector.Protect(id, Content) with { KeyId = "old" };

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(id, encrypted));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void InvalidKeyIsReportedOnlyWhenUsed(string key)
    {
        var protector = Protector(key);

        Assert.False(protector.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => protector.Protect(Guid.NewGuid(), Content));
    }

    [Fact]
    public void ValidKeyIsConfigured()
    {
        Assert.True(Protector().IsConfigured);
    }

    private static DocumentFileProtector Protector(string? key = null) => new(Options.Create(new DocumentStorageOptions
    {
        EncryptionKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
}

public sealed class DocumentFileValidatorTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];

    [Theory]
    [InlineData("front.jpg", "image/jpeg")]
    [InlineData("front.JPEG", "image/jpeg")]
    public async Task AcceptsImages(string name, string expected)
    {
        var file = Assert.Single(await Read(File(name, Jpeg)));

        Assert.Equal(expected, file.ContentType);
        Assert.Equal(name, file.FileName);
        Assert.Equal(Jpeg, file.Content);
    }

    [Fact]
    public async Task AcceptsPdfAndUpToFourImages()
    {
        Assert.Single(await Read(File("scan.pdf", Pdf(2))));
        Assert.Equal(4, (await Read(
            File("1.jpg", Jpeg), File("2.png", Png), File("3.jpg", Jpeg), File("4.jpg", Jpeg))).Count);
    }

    [Fact]
    public async Task StripsPathFromFileName()
    {
        var file = Assert.Single(await Read(File("../../front.jpg", Jpeg)));

        Assert.Equal("front.jpg", file.FileName);
    }

    [Fact]
    public Task RejectsNoFiles() => AssertRejected();

    [Fact]
    public Task RejectsMoreThanFourFiles() => AssertRejected(
        File("1.jpg", Jpeg), File("2.jpg", Jpeg), File("3.jpg", Jpeg), File("4.jpg", Jpeg), File("5.jpg", Jpeg));

    [Fact]
    public Task RejectsPdfMixedWithImages() => AssertRejected(File("scan.pdf", Pdf(1)), File("1.jpg", Jpeg));

    [Fact]
    public Task RejectsTwoPdfs() => AssertRejected(File("a.pdf", Pdf(1)), File("b.pdf", Pdf(1)));

    [Fact]
    public Task RejectsEmptyFile() => AssertRejected(File("empty.jpg", []));

    [Fact]
    public Task RejectsBmpWhichClaudeCannotRead() => AssertRejected(File("scan.bmp", [0x42, 0x4D, 1, 2, 3]));

    [Fact]
    public Task RejectsHeic() => AssertRejected(File("photo.heic", [0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]));

    [Fact]
    public Task RejectsSignatureMismatch() => AssertRejected(File("fake.jpg", Png));

    [Fact]
    public Task RejectsOversizedImage()
    {
        var content = new byte[DocumentFileValidator.MaxImageSize + 1];
        Jpeg.CopyTo(content, 0);
        return AssertRejected(File("large.jpg", content));
    }

    [Fact]
    public Task RejectsOversizedTotal()
    {
        var content = new byte[DocumentFileValidator.MaxTotalSize + 1];
        "%PDF-"u8.CopyTo(content);
        return AssertRejected(File("large.pdf", content));
    }

    [Fact]
    public async Task CountsPdfPages()
    {
        Assert.Equal(3, Assert.Single(await Read(File("scan.pdf", Pdf(3)))).PageCount);
    }

    [Fact]
    public Task RejectsPdfWithTooManyPages() => AssertRejected(File("long.pdf", Pdf(DocumentFileValidator.MaxPdfPages + 1)));

    [Fact]
    public async Task AcceptsPdfAtThePageLimit()
    {
        Assert.Single(await Read(File("long.pdf", Pdf(DocumentFileValidator.MaxPdfPages))));
    }

    [Fact]
    public Task RejectsCorruptPdf() => AssertRejected(File("broken.pdf", "%PDF-1.7 not really"u8.ToArray()));

    [Fact]
    public Task RejectsEncryptedPdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.SecuritySettings.UserPassword = "secret";
        document.SecuritySettings.OwnerPassword = "owner";
        using var stream = new MemoryStream();
        document.Save(stream);
        return AssertRejected(File("locked.pdf", stream.ToArray()));
    }

    [Fact]
    public void RequestValidatorRejectsOversizedUploadsBeforeReadingThem()
    {
        var validator = new DocumentUploadRequestValidator();

        validator.TestValidate(new DocumentAnalyseForm()).ShouldHaveValidationErrorFor(request => request.Files);
        validator.TestValidate(new DocumentAnalyseForm { Files = [File("a.jpg", [])] })
            .ShouldHaveValidationErrorFor(request => request.Files);
        validator.TestValidate(new DocumentAnalyseForm { Files = [File("a.jpg", Jpeg)] })
            .ShouldNotHaveAnyValidationErrors();
    }

    private static Task<IReadOnlyList<ValidatedDocumentFile>> Read(params IFormFile[] files) =>
        DocumentFileValidator.ReadAsync(files, default);

    private static Task AssertRejected(params IFormFile[] files) =>
        Assert.ThrowsAsync<InvalidDataException>(() => Read(files));

    private static IFormFile File(string name, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "files", name);

    private static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var page = 0; page < pages; page++) document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}

public sealed class DocumentCatalogueServiceTests
{
    [Fact]
    public async Task SeedsEveryListFromTheOnboardingRules()
    {
        await using var db = Database();

        await new DocumentCatalogueService(db).EnsureInitializedAsync(default);

        var requirements = await db.DocumentRequirements.Include(item => item.DocumentType).ToListAsync();
        Assert.Equal(14, await db.DocumentTypes.CountAsync());
        Assert.Equal(
            [DocumentTypeCodes.DrivingLicence, DocumentTypeCodes.Irp],
            await db.DocumentTypes.Where(type => type.RequiresBothSides).Select(type => type.Code).OrderBy(code => code).ToListAsync());
        Assert.Equal(8, requirements.Count(item => item.Category == DocumentCategory.CarCompany));
        Assert.Equal(11, requirements.Count(item => item.Category == DocumentCategory.MopedOwn));
        Assert.Equal(9, requirements.Count(item => item.Category == DocumentCategory.MopedCompany));
        Assert.Equal(6, requirements.Count(item => item.Category == DocumentCategory.EBikeOwn));
        Assert.Contains(requirements, item => item.Category == DocumentCategory.CarCompany && item.DocumentType.Code == DocumentTypeCodes.Obu);
        Assert.Equal(11, requirements.Count(item => item.Category == DocumentCategory.CarOwn));
        Assert.Equal(6, requirements.Count(item => item.Category == DocumentCategory.EBikeCompany));
        Assert.Equal(DocumentKind.Sign, (await db.DocumentTypes.SingleAsync(type => type.Code == DocumentTypeCodes.BikeSafety)).Kind);
        Assert.DoesNotContain(requirements, item => item.DocumentType.Code == DocumentTypeCodes.BikeSafety &&
            item.Category is DocumentCategory.CarOwn or DocumentCategory.CarCompany or DocumentCategory.InStore or DocumentCategory.Manager);
        Assert.Equal(5, requirements.Count(item => item.Category == DocumentCategory.InStore));
        Assert.Equal(5, requirements.Count(item => item.Category == DocumentCategory.Manager));
        Assert.All(requirements.Where(item => item.DocumentType.Code == DocumentTypeCodes.Irp),
            item => Assert.Equal(DocumentRequirementCondition.NonEuPassport, item.Condition));
        Assert.All(requirements.Where(item => item.DocumentType.Code == DocumentTypeCodes.CollegeLetter),
            item => Assert.Equal(DocumentRequirementCondition.Stamp2, item.Condition));
    }

    [Theory]
    [InlineData(DocumentTypeCodes.Spf, "Service Provider Form")]
    [InlineData(DocumentTypeCodes.Obu, "Business Use Insurance")]
    [InlineData(DocumentTypeCodes.PenaltyPoints, "Penalty points confirmation")]
    public async Task UsesTheAgreedDisplayNames(string code, string name)
    {
        await using var db = Database();

        await new DocumentCatalogueService(db).EnsureInitializedAsync(default);

        Assert.Equal(name, (await db.DocumentTypes.SingleAsync(type => type.Code == code)).Name);
    }

    [Fact]
    public async Task NeverRestoresWhatAnAdministratorRemoved()
    {
        await using var db = Database();
        var service = new DocumentCatalogueService(db);
        await service.EnsureInitializedAsync(default);
        db.DocumentRequirements.RemoveRange(db.DocumentRequirements);
        await db.SaveChangesAsync();

        await service.EnsureInitializedAsync(default);

        Assert.Empty(db.DocumentRequirements);
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);
}
