using Microsoft.AspNetCore.Http;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Contracts.SickLeave;

public sealed class SickLeaveCreateRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly FinishDate { get; set; }
    public IFormFile? File { get; set; }
}

public sealed class SickLeaveReviewRequest
{
    public string? Reason { get; set; }
}

public sealed class SickLeaveResponse
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly FinishDate { get; init; }
    public SickLeaveStatus Status { get; init; }
    public bool HasAttachment { get; init; }
    public string? AttachmentFileName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? ReviewedAtUtc { get; init; }
    public string? ReviewedByName { get; init; }
    public string? RejectionReason { get; init; }
    public DateTimeOffset? AttachmentDeletedAtUtc { get; init; }
}

public sealed class SickLeaveOverviewResponse
{
    public IReadOnlyList<SickLeaveResponse> Requested { get; init; } = [];
    public IReadOnlyList<SickLeaveResponse> Reviewed { get; init; } = [];
}
