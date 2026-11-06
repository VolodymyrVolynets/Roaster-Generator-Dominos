using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class AdminDocumentTests : IDisposable
{
    private static readonly byte[] Content = [0xFF, 0xD8, 0xFF, 9];

    private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly UserManager<ApplicationUser> users;
    private readonly DocumentFileProtector protector = new(Options.Create(new DocumentStorageOptions
    {
        EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly AdminDocumentService service;
    private readonly AdminDocumentsController controller;
    private readonly DocumentActor admin = new(Guid.NewGuid(), "admin", true);
    private readonly DocumentType passport = new()
    {
        Id = Guid.NewGuid(), Code = DocumentTypeCodes.Passport, Name = "Passport", ExpiryRule = DocumentExpiryRule.Printed, SortOrder = 10
    };
    private readonly DocumentType points = new()
    {
        Id = Guid.NewGuid(), Code = DocumentTypeCodes.PenaltyPoints, Name = "PP", ExpiryRule = DocumentExpiryRule.PenaltyPoints, SortOrder = 20
    };
    private readonly Employee driver = new()
    {
        Id = Guid.NewGuid(), EmployeeNumber = "100", FirstName = "Anna", LastName = "Driver", DriverProfile = new DriverProfile()
    };
    private readonly Employee storeWorker = new()
    {
        Id = Guid.NewGuid(), EmployeeNumber = "200", FirstName = "Ben", LastName = "Store", InStoreProfile = new InStoreProfile()
    };
    private readonly Employee inactive = new()
    {
        Id = Guid.NewGuid(), EmployeeNumber = "300", FirstName = "Cara", LastName = "Gone", IsActive = false, DriverProfile = new DriverProfile()
    };
    private readonly Employee removed = new()
    {
        Id = Guid.NewGuid(), EmployeeNumber = "400", FirstName = "Dan", LastName = "Removed", IsActive = false, IsDeleted = true
    };

    public AdminDocumentTests()
    {
        users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        service = new AdminDocumentService(db, users, protector, new DocumentVehicleService(db, TimeProvider.System), TimeProvider.System, NullLogger<AdminDocumentService>.Instance);
        controller = new AdminDocumentsController(service, users, new DocumentAdminUpdateRequestValidator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        db.Employees.AddRange(driver, storeWorker, inactive, removed);
        db.DocumentTypes.AddRange(passport, points);
        db.DocumentRequirements.Add(new DocumentRequirement { Category = DocumentCategory.CarOwn, DocumentTypeId = passport.Id });
        db.DocumentRequirements.Add(new DocumentRequirement { Category = DocumentCategory.InStore, DocumentTypeId = passport.Id });
        var driverRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Driver };
        var storeRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.InStore };
        db.Roles.AddRange(driverRole, storeRole);
        foreach (var (employee, role) in new[] { (driver, driverRole), (storeWorker, storeRole), (inactive, driverRole) })
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = employee.EmployeeNumber, EmployeeId = employee.Id };
            db.Users.Add(user);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        }

        db.SaveChanges();
    }

    public void Dispose()
    {
        users.Dispose();
        db.Dispose();
        services.Dispose();
    }

    [Fact]
    public void EveryRouteRequiresTheAdminPolicy()
    {
        Assert.Equal(AuthorizationPolicies.Admin, typeof(AdminDocumentsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.DoesNotContain(typeof(AdminDocumentsController).GetMethods(),
            method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    [Fact]
    public async Task ListsActiveEmployeesWithMissingDocuments()
    {
        var result = await service.ListAsync(new AdminDocumentFilters(), default);

        Assert.Equal(["Anna Driver", "Ben Store"], result.Groups.Select(group => group.EmployeeName));
        var group = result.Groups[0];
        Assert.Equal(1, group.Outstanding);
        Assert.Equal(DocumentRowState.NotUploaded, Assert.Single(group.Documents).State);
    }

    [Fact]
    public async Task IncludesInactiveOnRequestButNeverRemovedEmployees()
    {
        var result = await service.ListAsync(new AdminDocumentFilters { IncludeInactive = true }, default);

        Assert.Equal(["Anna Driver", "Ben Store", "Cara Gone"], result.Groups.Select(group => group.EmployeeName));
    }

    [Theory]
    [InlineData("anna")]
    [InlineData("100")]
    [InlineData("ab-123")]
    public async Task SearchesNameNumberAndReference(string search)
    {
        await AddDocument(driver, passport, reference: "AB-123");

        var result = await service.ListAsync(new AdminDocumentFilters { Search = search }, default);

        Assert.Equal(driver.Id, Assert.Single(result.Groups).EmployeeId);
    }

    [Fact]
    public async Task FiltersByStateAndHidesEmployeesWithNoMatch()
    {
        await AddDocument(driver, passport, review: DocumentReviewStatus.NeedsReview, reasons: ["Glare"]);

        var result = await service.ListAsync(new AdminDocumentFilters { State = DocumentRowState.WaitingForReview }, default);

        var group = Assert.Single(result.Groups);
        Assert.Equal(1, group.NeedingReview);
        Assert.True(Assert.Single(group.Documents).NeedsReview);
    }

    [Fact]
    public async Task FiltersByType()
    {
        await AddDocument(driver, points);

        var result = await service.ListAsync(new AdminDocumentFilters { DocumentTypeId = points.Id }, default);

        Assert.Equal(points.Id, Assert.Single(Assert.Single(result.Groups).Documents).DocumentTypeId);
    }

    [Fact]
    public async Task DetailIncludesReasonsFilesAndAudit()
    {
        var document = await AddDocument(driver, passport, review: DocumentReviewStatus.NeedsReview, reasons: ["Glare"]);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        await service.ReadFileAsync(document.Id, file.Id, preview: true, admin, default);

        var detail = await service.GetAsync(document.Id, default);

        Assert.Equal("Anna Driver", detail!.EmployeeName);
        Assert.Equal(["Glare"], detail.ReviewReasons);
        Assert.Equal(file.Id, Assert.Single(detail.Files).Id);
        Assert.Equal(DocumentAuditAction.Viewed, Assert.Single(detail.Audit).Action);
    }

    [Theory]
    [InlineData(true, DocumentAuditAction.Viewed)]
    [InlineData(false, DocumentAuditAction.Downloaded)]
    public async Task ReadingAFileDecryptsItAndRecordsTheAccess(bool preview, DocumentAuditAction expected)
    {
        var document = await AddDocument(driver, passport);
        var file = await db.EmployeeDocumentFiles.SingleAsync();

        var result = await service.ReadFileAsync(document.Id, file.Id, preview, admin, default);

        Assert.Equal(Content, result!.Content);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal("Anna Passport 01.01.35.jpg", result.FileName);
        var audit = await db.DocumentAuditEvents.SingleAsync();
        Assert.Equal(expected, audit.Action);
        Assert.Equal("admin", audit.ActorName);
    }

    [Fact]
    public async Task FileMustBelongToTheDocument()
    {
        var first = await AddDocument(driver, passport);
        var other = await AddDocument(storeWorker, passport);
        var otherFile = await db.EmployeeDocumentFiles.SingleAsync(file => file.DocumentId == other.Id);

        Assert.Null(await service.ReadFileAsync(first.Id, otherFile.Id, false, admin, default));
        Assert.Empty(db.DocumentAuditEvents);
    }

    [Fact]
    public async Task DownloadSetsSafeHeaders()
    {
        var document = await AddDocument(driver, passport);
        var file = await db.EmployeeDocumentFiles.SingleAsync();

        var result = Assert.IsType<FileContentResult>(await controller.ReadFile(document.Id, file.Id, false, default));

        Assert.Equal("Anna Passport 01.01.35.jpg", result.FileDownloadName);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
    }

    [Fact]
    public async Task TamperedFileReportsAnErrorAndIsNotAudited()
    {
        var document = await AddDocument(driver, passport);
        var file = await db.EmployeeDocumentFiles.SingleAsync();
        var tampered = file.EncryptedContent.ToArray();
        tampered[0] ^= 0xFF;
        file.EncryptedContent = tampered;
        await db.SaveChangesAsync();

        var result = Assert.IsType<ObjectResult>(await controller.ReadFile(document.Id, file.Id, false, default));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Empty(db.DocumentAuditEvents);
    }

    [Fact]
    public async Task SavingClearsReviewAndRecordsIt()
    {
        var document = await AddDocument(driver, passport, review: DocumentReviewStatus.NeedsReview, reasons: ["Glare"]);

        var updated = await service.UpdateAsync(document.Id, new DocumentAdminUpdateRequest
        {
            ReferenceNumber = "  NEW1 ", IssuingCountry = "ie", ExpiryDate = new DateOnly(2031, 1, 1)
        }, admin, default);

        Assert.False(updated!.NeedsReview);
        Assert.Empty(updated.ReviewReasons);
        Assert.Equal("NEW1", updated.Fields.ReferenceNumber);
        Assert.Equal("IE", updated.Fields.IssuingCountry);
        Assert.Equal(1, updated.Revision);
        Assert.Equal(DocumentAuditAction.Reviewed, Assert.Single(updated.Audit).Action);
    }

    [Fact]
    public async Task EditingAClearDocumentIsRecordedAsAnEdit()
    {
        var document = await AddDocument(driver, passport);

        var updated = await service.UpdateAsync(document.Id, new DocumentAdminUpdateRequest(), admin, default);

        Assert.Equal(DocumentAuditAction.Edited, Assert.Single(updated!.Audit).Action);
    }

    [Fact]
    public async Task StaleEditIsRefused()
    {
        var document = await AddDocument(driver, passport);
        await service.UpdateAsync(document.Id, new DocumentAdminUpdateRequest(), admin, default);

        Assert.IsType<ConflictObjectResult>(await controller.Update(
            document.Id, new DocumentAdminUpdateRequest { Revision = 0 }, default));
    }

    [Fact]
    public async Task PenaltyPointsExpiryIsRecalculatedOnEdit()
    {
        var document = await AddDocument(driver, points);

        var updated = await service.UpdateAsync(document.Id, new DocumentAdminUpdateRequest
        {
            SigningDate = new DateOnly(2026, 1, 15), PenaltyPoints = 4, ExpiryDate = new DateOnly(2040, 1, 1)
        }, admin, default);

        Assert.Equal(new DateOnly(2026, 2, 15), updated!.Fields.ExpiryDate);
    }

    [Fact]
    public async Task InvalidCountryIsRejected()
    {
        var document = await AddDocument(driver, passport);

        Assert.IsType<BadRequestObjectResult>(await controller.Update(
            document.Id, new DocumentAdminUpdateRequest { IssuingCountry = "Ireland" }, default));
    }

    [Fact]
    public async Task DeleteRemovesTheFilesAndKeepsTheAudit()
    {
        var document = await AddDocument(driver, passport);

        Assert.IsType<NoContentResult>(await controller.Delete(document.Id, default));

        Assert.Empty(db.EmployeeDocuments);
        Assert.Empty(db.EmployeeDocumentFiles);
        Assert.Equal(DocumentAuditAction.Deleted, (await db.DocumentAuditEvents.SingleAsync()).Action);
    }

    [Fact]
    public async Task UnknownDocumentIsNotFound()
    {
        var id = Guid.NewGuid();

        Assert.IsType<NotFoundObjectResult>(await controller.Get(id, default));
        Assert.IsType<NotFoundObjectResult>(await controller.Delete(id, default));
        Assert.IsType<NotFoundObjectResult>(await controller.ReadFile(id, Guid.NewGuid(), false, default));
        Assert.IsType<NotFoundObjectResult>(await controller.Update(id, new DocumentAdminUpdateRequest(), default));
    }

    private async Task<EmployeeDocument> AddDocument(
        Employee employee,
        DocumentType type,
        string? reference = null,
        DocumentReviewStatus review = DocumentReviewStatus.Clear,
        List<string>? reasons = null)
    {
        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            DocumentTypeId = type.Id,
            ReferenceNumber = reference,
            ExpiryDate = new DateOnly(2035, 1, 1),
            ReviewStatus = review,
            ReviewReasons = reasons ?? []
        };
        var id = Guid.NewGuid();
        var encrypted = protector.Protect(id, Content);
        db.EmployeeDocuments.Add(document);
        db.EmployeeDocumentFiles.Add(new EmployeeDocumentFile
        {
            Id = id,
            DocumentId = document.Id,
            FileName = "front.jpg",
            ContentType = "image/jpeg",
            Size = Content.Length,
            EncryptedContent = encrypted.Ciphertext,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            KeyId = encrypted.KeyId
        });
        await db.SaveChangesAsync();
        return document;
    }
}

public sealed class DocumentFileNameTests
{
    private static readonly DateOnly Expiry = new(2030, 12, 31);

    [Theory]
    [InlineData(DocumentTypeCodes.Passport, "Anna Passport Ireland 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.DrivingLicence, "Anna License 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.Irp, "Anna IRP Stamp 2 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.LabourMarketPermission, "Anna LMP 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.TemporaryResidenceCertificate, "Anna TRC 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.Spf, "Anna SPF.pdf")]
    [InlineData(DocumentTypeCodes.FoodHandler, "Anna Food handler.pdf")]
    [InlineData(DocumentTypeCodes.PenaltyPoints, "Anna PP 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.Obu, "Anna OBU 241-D-12345.pdf")]
    [InlineData(DocumentTypeCodes.Nct, "Anna NCT 241-D-12345 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.MotorTax, "Anna Tax 241-D-12345 31.12.30.pdf")]
    [InlineData(DocumentTypeCodes.Insurance, "Anna Insurance 241-D-12345 31.12.30.pdf")]
    [InlineData("forklift", "Anna Forklift licence 241-D-12345 31.12.30.pdf")]
    public void FollowsTheOnboardingConvention(string code, string expected)
    {
        Assert.Equal(expected, DocumentFileNames.Build("Anna", Document(code), ".pdf", 0, 1));
    }

    [Theory]
    [InlineData(0, 2, "Anna IRP Stamp 2 31.12.30 front.jpg")]
    [InlineData(1, 2, "Anna IRP Stamp 2 31.12.30 back.jpg")]
    [InlineData(2, 3, "Anna IRP Stamp 2 31.12.30 page 3.jpg")]
    public void LabelsMultiplePages(int index, int count, string expected)
    {
        Assert.Equal(expected, DocumentFileNames.Build("Anna", Document(DocumentTypeCodes.Irp), ".jpg", index, count));
    }

    [Fact]
    public void LeavesOutFieldsTheDocumentDoesNotHave()
    {
        var document = new EmployeeDocument { DocumentType = new DocumentType { Code = DocumentTypeCodes.Passport } };

        Assert.Equal("Anna Passport.pdf", DocumentFileNames.Build("Anna", document, ".pdf", 0, 1));
    }

    [Fact]
    public void NeverIncludesTheReferenceNumber()
    {
        Assert.DoesNotContain("SECRET1", DocumentFileNames.Build("Anna", Document(DocumentTypeCodes.Passport), ".pdf", 0, 1));
    }

    [Fact]
    public void RemovesPathCharacters()
    {
        var document = Document(DocumentTypeCodes.Obu);
        document.VehicleRegistration = "../12:3";

        Assert.Equal("Anna OBU .. 12 3.pdf", DocumentFileNames.Build("Anna", document, ".pdf", 0, 1));
    }

    private static EmployeeDocument Document(string code) => new()
    {
        DocumentType = new DocumentType { Code = code, Name = "Forklift licence" },
        ReferenceNumber = "SECRET1",
        IssuingCountry = "IE",
        ExpiryDate = Expiry,
        Stamp = "Stamp 2",
        VehicleRegistration = "241-D-12345"
    };
}
