using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Data.Configurations;

public sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("document_types");
        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(type => type.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(type => type.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(type => type.Kind).HasColumnName("kind").HasConversion<string>()
            .HasMaxLength(16).HasDefaultValue(DocumentKind.Upload).IsRequired();
        builder.Property(type => type.ExpiryRule).HasColumnName("expiry_rule").HasConversion<string>()
            .HasMaxLength(16).IsRequired();
        builder.Property(type => type.HasReference).HasColumnName("has_reference").IsRequired();
        builder.Property(type => type.RequiresBothSides).HasColumnName("requires_both_sides").IsRequired();
        builder.Property(type => type.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(type => type.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.HasIndex(type => type.Code).IsUnique();
    }
}

public sealed class DocumentRequirementConfiguration : IEntityTypeConfiguration<DocumentRequirement>
{
    public void Configure(EntityTypeBuilder<DocumentRequirement> builder)
    {
        builder.ToTable("document_requirements");
        builder.HasKey(requirement => requirement.Id);
        builder.Property(requirement => requirement.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(requirement => requirement.Category).HasColumnName("category").HasConversion<string>()
            .HasMaxLength(16).IsRequired();
        builder.Property(requirement => requirement.DocumentTypeId).HasColumnName("document_type_id")
            .HasColumnType("uuid").IsRequired();
        builder.Property(requirement => requirement.Condition).HasColumnName("condition").HasConversion<string>()
            .HasMaxLength(16).IsRequired();
        builder.HasIndex(requirement => new { requirement.Category, requirement.DocumentTypeId }).IsUnique();
        builder.HasOne(requirement => requirement.DocumentType).WithMany()
            .HasForeignKey(requirement => requirement.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(document => document.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").IsRequired();
        builder.Property(document => document.DocumentTypeId).HasColumnName("document_type_id").HasColumnType("uuid").IsRequired();
        builder.Property(document => document.HolderName).HasColumnName("holder_name").HasMaxLength(200);
        builder.Property(document => document.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(100);
        builder.Property(document => document.IssuingCountry).HasColumnName("issuing_country").HasMaxLength(2);
        builder.Property(document => document.IssueDate).HasColumnName("issue_date").HasColumnType("date");
        builder.Property(document => document.ExpiryDate).HasColumnName("expiry_date").HasColumnType("date");
        builder.Property(document => document.Stamp).HasColumnName("stamp").HasMaxLength(20);
        builder.Property(document => document.VehicleRegistration).HasColumnName("vehicle_registration").HasMaxLength(20);
        builder.Property(document => document.SigningDate).HasColumnName("signing_date").HasColumnType("date");
        builder.Property(document => document.PenaltyPoints).HasColumnName("penalty_points");
        builder.Property(document => document.ReviewStatus).HasColumnName("review_status").HasConversion<string>()
            .HasMaxLength(16).HasDefaultValue(DocumentReviewStatus.Clear).IsRequired();
        builder.Property(document => document.ReviewReasons).HasColumnName("review_reasons").IsRequired();
        builder.Property(document => document.EmployeeNote).HasColumnName("employee_note").HasMaxLength(500);
        builder.Property(document => document.NewSignatureRequested).HasColumnName("new_signature_requested").IsRequired();
        builder.Property(document => document.RedoReason).HasColumnName("redo_reason").HasMaxLength(500);
        builder.Property(document => document.AwaitingManagerSignature).HasColumnName("awaiting_manager_signature").IsRequired();
        builder.Property(document => document.ManagerSignedByName).HasColumnName("manager_signed_by_name").HasMaxLength(200);
        builder.Property(document => document.ManagerSignedAtUtc).HasColumnName("manager_signed_at_utc");
        builder.Property(document => document.UploadedAtUtc).HasColumnName("uploaded_at_utc").IsRequired();
        builder.Property(document => document.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        builder.Property(document => document.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(document => document.Revision).HasColumnName("revision").IsConcurrencyToken().IsRequired();
        builder.HasIndex(document => new { document.EmployeeId, document.DocumentTypeId }).IsUnique();
        builder.HasIndex(document => document.ExpiryDate);
        builder.HasIndex(document => document.ReviewStatus);
        builder.HasOne(document => document.Employee).WithMany()
            .HasForeignKey(document => document.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(document => document.DocumentType).WithMany()
            .HasForeignKey(document => document.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DocumentDraftConfiguration : IEntityTypeConfiguration<DocumentDraft>
{
    public void Configure(EntityTypeBuilder<DocumentDraft> builder)
    {
        builder.ToTable("document_drafts");
        builder.HasKey(draft => draft.Id);
        builder.Property(draft => draft.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(draft => draft.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").IsRequired();
        builder.Property(draft => draft.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(draft => draft.AnalysisResult).HasColumnName("analysis_result").HasColumnType("jsonb");
        builder.Property(draft => draft.PageCount).HasColumnName("page_count").HasDefaultValue(1).IsRequired();
        builder.Property(draft => draft.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(draft => draft.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.HasIndex(draft => draft.ExpiresAtUtc);
        builder.HasOne(draft => draft.Employee).WithMany()
            .HasForeignKey(draft => draft.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeeDocumentFileConfiguration : IEntityTypeConfiguration<EmployeeDocumentFile>
{
    public void Configure(EntityTypeBuilder<EmployeeDocumentFile> builder)
    {
        builder.ToTable("employee_document_files", table => table.HasCheckConstraint(
            "ck_employee_document_files_one_owner", "(document_id IS NULL) <> (draft_id IS NULL)"));
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(file => file.DocumentId).HasColumnName("document_id").HasColumnType("uuid");
        builder.Property(file => file.DraftId).HasColumnName("draft_id").HasColumnType("uuid");
        builder.Property(file => file.PageIndex).HasColumnName("page_index").IsRequired();
        builder.Property(file => file.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(file => file.ContentType).HasColumnName("content_type").HasMaxLength(64).IsRequired();
        builder.Property(file => file.Size).HasColumnName("size").IsRequired();
        builder.Property(file => file.EncryptedContent).HasColumnName("encrypted_content").HasColumnType("bytea").IsRequired();
        builder.Property(file => file.Nonce).HasColumnName("nonce").HasColumnType("bytea").IsRequired();
        builder.Property(file => file.Tag).HasColumnName("tag").HasColumnType("bytea").IsRequired();
        builder.Property(file => file.KeyId).HasColumnName("key_id").HasMaxLength(32).IsRequired();
        builder.HasIndex(file => new { file.DocumentId, file.PageIndex }).IsUnique();
        builder.HasIndex(file => new { file.DraftId, file.PageIndex }).IsUnique();
        builder.HasOne(file => file.Document).WithMany(document => document.Files)
            .HasForeignKey(file => file.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(file => file.Draft).WithMany(draft => draft.Files)
            .HasForeignKey(file => file.DraftId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentAuditEventConfiguration : IEntityTypeConfiguration<DocumentAuditEvent>
{
    public void Configure(EntityTypeBuilder<DocumentAuditEvent> builder)
    {
        builder.ToTable("document_audit_events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").IsRequired();
        builder.Property(item => item.DocumentId).HasColumnName("document_id").HasColumnType("uuid");
        builder.Property(item => item.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(item => item.ActorName).HasColumnName("actor_name").HasMaxLength(256).IsRequired();
        builder.Property(item => item.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(item => item.DocumentTypeCode).HasColumnName("document_type_code").HasMaxLength(40).IsRequired();
        builder.Property(item => item.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.HasIndex(item => new { item.EmployeeId, item.OccurredAtUtc });
        builder.HasIndex(item => item.DocumentId);
    }
}

public sealed class EmployeeVehicleConfiguration : IEntityTypeConfiguration<EmployeeVehicle>
{
    public void Configure(EntityTypeBuilder<EmployeeVehicle> builder)
    {
        builder.ToTable("employee_vehicles");
        builder.HasKey(vehicle => vehicle.EmployeeId);
        builder.Property(vehicle => vehicle.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(vehicle => vehicle.Registration).HasColumnName("registration").HasMaxLength(20).IsRequired();
        builder.Property(vehicle => vehicle.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.HasOne(vehicle => vehicle.Employee).WithOne()
            .HasForeignKey<EmployeeVehicle>(vehicle => vehicle.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class VehicleChangeConfiguration : IEntityTypeConfiguration<VehicleChange>
{
    public void Configure(EntityTypeBuilder<VehicleChange> builder)
    {
        builder.ToTable("vehicle_changes");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).HasColumnName("id").HasColumnType("uuid")
            .ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(change => change.EmployeeId).HasColumnName("employee_id").HasColumnType("uuid").IsRequired();
        builder.Property(change => change.OldRegistration).HasColumnName("old_registration").HasMaxLength(20).IsRequired();
        builder.Property(change => change.NewRegistration).HasColumnName("new_registration").HasMaxLength(20).IsRequired();
        builder.Property(change => change.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(change => change.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(change => change.StartedByUserId).HasColumnName("started_by_user_id");
        builder.Property(change => change.StartedByName).HasColumnName("started_by_name").HasMaxLength(256).IsRequired();
        builder.Property(change => change.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.HasIndex(change => change.EmployeeId).IsUnique().HasFilter("status = 'Open'");
        builder.HasOne(change => change.Employee).WithMany()
            .HasForeignKey(change => change.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates");
        builder.HasKey(template => template.DocumentTypeId);
        builder.Property(template => template.DocumentTypeId).HasColumnName("document_type_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(template => template.Content).HasColumnName("content").HasColumnType("bytea").IsRequired();
        builder.Property(template => template.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(template => template.Fields).HasColumnName("fields").HasColumnType("jsonb").IsRequired();
        builder.Property(template => template.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.HasOne(template => template.DocumentType).WithOne()
            .HasForeignKey<DocumentTemplate>(template => template.DocumentTypeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentManagerSignatureConfiguration : IEntityTypeConfiguration<DocumentManagerSignature>
{
    public void Configure(EntityTypeBuilder<DocumentManagerSignature> builder)
    {
        builder.ToTable("document_manager_signatures");
        builder.HasKey(signature => signature.Id);
        builder.Property(signature => signature.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(signature => signature.ManagerName).HasColumnName("manager_name").HasMaxLength(200).IsRequired();
        builder.Property(signature => signature.ManagerEmployeeId).HasColumnName("manager_employee_id").HasColumnType("uuid");
        builder.Property(signature => signature.EncryptedContent).HasColumnName("encrypted_content").HasColumnType("bytea").IsRequired();
        builder.Property(signature => signature.Nonce).HasColumnName("nonce").HasColumnType("bytea").IsRequired();
        builder.Property(signature => signature.Tag).HasColumnName("tag").HasColumnType("bytea").IsRequired();
        builder.Property(signature => signature.KeyId).HasColumnName("key_id").HasMaxLength(32).IsRequired();
        builder.Property(signature => signature.SavedAtUtc).HasColumnName("saved_at_utc").IsRequired();
    }
}
