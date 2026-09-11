using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class SickLeaveRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly FinishDate { get; set; }
    public SickLeaveStatus Status { get; set; } = SickLeaveStatus.Requested;
    public byte[]? AttachmentContent { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentFileSize { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? AttachmentDeletedAtUtc { get; set; }
    public int Revision { get; set; }
}
