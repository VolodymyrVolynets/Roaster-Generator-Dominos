using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class DocumentSettingsTests : IDisposable
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DocumentSettingsService service;
    private readonly AdminDocumentSettingsController controller;

    public DocumentSettingsTests()
    {
        service = new DocumentSettingsService(db);
        controller = new AdminDocumentSettingsController(
            service, new DocumentTypeRequestValidator(), new DocumentRequirementListRequestValidator());
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public void EveryEndpointRequiresTheAdminPolicy()
    {
        var attribute = Assert.Single(typeof(AdminDocumentSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.Admin, attribute.Policy);
        Assert.DoesNotContain(typeof(AdminDocumentSettingsController).GetMethods(),
            method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);
    }

    [Fact]
    public async Task CreatesTypeWithStableCodeAtTheEndOfTheCatalogue()
    {
        await AddType("existing", sortOrder: 40);

        var created = await service.CreateTypeAsync(Request("  Forklift Licence (2026) "), default);

        Assert.Equal("Forklift Licence (2026)", created.Name);
        Assert.Equal("forklift-licence-2026", created.Code);
        Assert.Equal(50, created.SortOrder);
        Assert.False(created.IsBuiltIn);
    }

    [Fact]
    public async Task RenamingKeepsTheCode()
    {
        var created = await service.CreateTypeAsync(Request("Forklift"), default);

        var updated = await service.UpdateTypeAsync(created.Id, Request("Forklift licence"), default);

        Assert.Equal("Forklift licence", updated!.Name);
        Assert.Equal("forklift", updated.Code);
    }

    [Fact]
    public async Task CodeClashGetsASuffix()
    {
        await AddType("forklift", name: "Something else");

        Assert.Equal("forklift-2", (await service.CreateTypeAsync(Request("Forklift"), default)).Code);
    }

    [Fact]
    public async Task NameWithoutLatinLettersStillGetsACode()
    {
        Assert.Equal("document", (await service.CreateTypeAsync(Request("Паспорт"), default)).Code);
    }

    [Fact]
    public async Task VeryLongNameFitsTheCodeColumn()
    {
        var name = new string('a', 100);
        await service.CreateTypeAsync(Request(name), default);

        var second = await service.CreateTypeAsync(Request(name[..99] + "b"), default);

        Assert.True(second.Code.Length <= 40);
    }

    [Fact]
    public async Task RejectsDuplicateNameIgnoringCase()
    {
        await service.CreateTypeAsync(Request("Forklift"), default);

        await Assert.ThrowsAsync<DocumentSettingsException>(
            () => service.CreateTypeAsync(Request("FORKLIFT"), default));
    }

    [Fact]
    public async Task UpdateMayKeepItsOwnName()
    {
        var created = await service.CreateTypeAsync(Request("Forklift"), default);

        var updated = await service.UpdateTypeAsync(
            created.Id, new DocumentTypeRequest { Name = "Forklift", IsActive = false }, default);

        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task DuplicateNameIsReportedAsValidationError()
    {
        await service.CreateTypeAsync(Request("Forklift"), default);

        Assert.IsType<BadRequestObjectResult>(await controller.CreateType(Request("Forklift"), default));
    }

    [Fact]
    public async Task UpdatingUnknownTypeIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(await controller.UpdateType(Guid.NewGuid(), Request("Anything"), default));
    }

    [Fact]
    public async Task DeletesUnusedType()
    {
        var created = await service.CreateTypeAsync(Request("Forklift"), default);

        Assert.IsType<NoContentResult>(await controller.DeleteType(created.Id, default));
        Assert.Empty(db.DocumentTypes);
    }

    [Fact]
    public async Task BuiltInTypeCannotBeDeleted()
    {
        var type = await AddType(DocumentTypeCodes.Passport);

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteType(type.Id, default));
        Assert.Single(db.DocumentTypes);
    }

    [Fact]
    public async Task TypeOnARequiredListCannotBeDeleted()
    {
        var type = await AddType("forklift");
        await service.SaveListAsync(DocumentCategory.InStore, List(type.Id), default);

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteType(type.Id, default));
    }

    [Fact]
    public async Task TypeWithUploadedDocumentsCannotBeDeleted()
    {
        var type = await AddType("forklift");
        db.EmployeeDocuments.Add(new EmployeeDocument { EmployeeId = Guid.NewGuid(), DocumentTypeId = type.Id });
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteType(type.Id, default));
    }

    [Fact]
    public async Task GetListsReturnsEveryCategoryInCatalogueOrder()
    {
        var second = await AddType("second", sortOrder: 20);
        var first = await AddType("first", sortOrder: 10);
        await service.SaveListAsync(DocumentCategory.MopedOwn, List(second.Id, first.Id), default);

        var lists = await service.GetListsAsync(default);

        Assert.Equal(Enum.GetValues<DocumentCategory>(), lists.Select(list => list.Category));
        Assert.Equal([first.Id, second.Id],
            lists.Single(list => list.Category == DocumentCategory.MopedOwn).Items.Select(item => item.DocumentTypeId));
        Assert.Empty(lists.Single(list => list.Category == DocumentCategory.CarOwn).Items);
    }

    [Fact]
    public async Task SavingReplacesOnlyThatList()
    {
        var kept = await AddType("kept");
        var replaced = await AddType("replaced");
        await service.SaveListAsync(DocumentCategory.CarOwn, List(kept.Id), default);
        await service.SaveListAsync(DocumentCategory.CarCompany, List(kept.Id, replaced.Id), default);

        var saved = await service.SaveListAsync(DocumentCategory.CarCompany, new DocumentRequirementListRequest
        {
            Items = [new() { DocumentTypeId = replaced.Id, Condition = DocumentRequirementCondition.NonEuPassport }]
        }, default);

        Assert.Equal(DocumentRequirementCondition.NonEuPassport, Assert.Single(saved.Items).Condition);
        Assert.Equal(1, await db.DocumentRequirements.CountAsync(item => item.Category == DocumentCategory.CarCompany));
        Assert.Equal(kept.Id, (await db.DocumentRequirements.SingleAsync(item => item.Category == DocumentCategory.CarOwn)).DocumentTypeId);
    }

    [Fact]
    public async Task ListCanBeEmptied()
    {
        var type = await AddType("forklift");
        await service.SaveListAsync(DocumentCategory.CarOwn, List(type.Id), default);

        var saved = await service.SaveListAsync(DocumentCategory.CarOwn, new DocumentRequirementListRequest(), default);

        Assert.Empty(saved.Items);
        Assert.Empty(db.DocumentRequirements);
    }

    [Fact]
    public async Task UnknownTypeLeavesTheListUntouched()
    {
        var type = await AddType("forklift");
        await service.SaveListAsync(DocumentCategory.CarOwn, List(type.Id), default);

        var result = await controller.SaveList(DocumentCategory.CarOwn, List(Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(type.Id, (await db.DocumentRequirements.SingleAsync()).DocumentTypeId);
    }

    [Fact]
    public async Task UndefinedCategoryIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(await controller.SaveList((DocumentCategory)99, List(), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TypeNameIsRequired(string name)
    {
        new DocumentTypeRequestValidator().TestValidate(Request(name))
            .ShouldHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void TypeNameHasALimit()
    {
        new DocumentTypeRequestValidator().TestValidate(Request(new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void RejectsUndefinedExpiryRule()
    {
        new DocumentTypeRequestValidator().TestValidate(new DocumentTypeRequest { Name = "A", ExpiryRule = (DocumentExpiryRule)9 })
            .ShouldHaveValidationErrorFor(request => request.ExpiryRule);
    }

    [Fact]
    public void RejectsDuplicateTypeInAList()
    {
        var id = Guid.NewGuid();

        new DocumentRequirementListRequestValidator().TestValidate(List(id, id))
            .ShouldHaveValidationErrorFor(request => request.Items);
    }

    [Fact]
    public void RejectsUndefinedCondition()
    {
        var request = new DocumentRequirementListRequest
        {
            Items = [new() { DocumentTypeId = Guid.NewGuid(), Condition = (DocumentRequirementCondition)9 }]
        };

        Assert.False(new DocumentRequirementListRequestValidator().Validate(request).IsValid);
    }

    private static DocumentTypeRequest Request(string name) => new() { Name = name };

    private static DocumentRequirementListRequest List(params Guid[] ids) => new()
    {
        Items = ids.Select(id => new DocumentRequirementItem { DocumentTypeId = id }).ToList()
    };

    private async Task<DocumentType> AddType(string code, string? name = null, int sortOrder = 10)
    {
        var type = new DocumentType { Id = Guid.NewGuid(), Code = code, Name = name ?? code, SortOrder = sortOrder };
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }
}
