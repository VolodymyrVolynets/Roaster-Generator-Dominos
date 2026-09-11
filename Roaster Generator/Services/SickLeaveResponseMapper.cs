using System.Linq.Expressions;
using Roaster_Generator.Contracts.SickLeave;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public static class SickLeaveResponseMapper
{
    public static readonly Expression<Func<SickLeaveRequest, SickLeaveResponse>> Projection = request => new SickLeaveResponse
    {
        Id = request.Id,
        EmployeeId = request.EmployeeId,
        EmployeeName = request.Employee.FirstName + " " + request.Employee.LastName,
        EmployeeNumber = request.Employee.EmployeeNumber,
        StartDate = request.StartDate,
        FinishDate = request.FinishDate,
        Status = request.Status,
        HasAttachment = request.AttachmentContent != null,
        AttachmentFileName = request.AttachmentFileName,
        CreatedAtUtc = request.CreatedAtUtc,
        UpdatedAtUtc = request.UpdatedAtUtc,
        ReviewedAtUtc = request.ReviewedAtUtc,
        ReviewedByName = request.ReviewedByName,
        RejectionReason = request.RejectionReason,
        AttachmentDeletedAtUtc = request.AttachmentDeletedAtUtc
    };

    public static SickLeaveResponse ToResponse(SickLeaveRequest request) => new()
    {
        Id = request.Id,
        EmployeeId = request.EmployeeId,
        EmployeeName = $"{request.Employee.FirstName} {request.Employee.LastName}".Trim(),
        EmployeeNumber = request.Employee.EmployeeNumber,
        StartDate = request.StartDate,
        FinishDate = request.FinishDate,
        Status = request.Status,
        HasAttachment = request.AttachmentContent is not null,
        AttachmentFileName = request.AttachmentFileName,
        CreatedAtUtc = request.CreatedAtUtc,
        UpdatedAtUtc = request.UpdatedAtUtc,
        ReviewedAtUtc = request.ReviewedAtUtc,
        ReviewedByName = request.ReviewedByName,
        RejectionReason = request.RejectionReason,
        AttachmentDeletedAtUtc = request.AttachmentDeletedAtUtc
    };
}
