using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Data.Configurations;

public sealed class SickLeaveRequestConfiguration : IEntityTypeConfiguration<SickLeaveRequest>
{
    public void Configure(EntityTypeBuilder<SickLeaveRequest> builder)
    {
        builder.ToTable("sick_leave_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(request => request.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").IsRequired();
        builder.Property(request => request.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        builder.Property(request => request.FinishDate).HasColumnName("finish_date").HasColumnType("date").IsRequired();
        builder.Property(request => request.Status).HasColumnName("status").HasConversion<string>()
            .HasMaxLength(16).HasDefaultValue(SickLeaveStatus.Requested).IsRequired();
        builder.Property(request => request.AttachmentContent).HasColumnName("attachment_content").HasColumnType("bytea");
        builder.Property(request => request.AttachmentFileName).HasColumnName("attachment_file_name").HasMaxLength(255);
        builder.Property(request => request.AttachmentContentType).HasColumnName("attachment_content_type").HasMaxLength(64);
        builder.Property(request => request.AttachmentFileSize).HasColumnName("attachment_file_size");
        builder.Property(request => request.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(request => request.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(request => request.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(request => request.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(request => request.ReviewedByName).HasColumnName("reviewed_by_name").HasMaxLength(256);
        builder.Property(request => request.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(request => request.AttachmentDeletedAtUtc).HasColumnName("attachment_deleted_at_utc");
        builder.Property(request => request.Revision).HasColumnName("revision").IsConcurrencyToken().IsRequired();
        builder.HasIndex(request => new { request.EmployeeId, request.Status });
        builder.HasIndex(request => new { request.StartDate, request.FinishDate });
        builder.HasOne(request => request.Employee).WithMany(employee => employee.SickLeaveRequests)
            .HasForeignKey(request => request.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
