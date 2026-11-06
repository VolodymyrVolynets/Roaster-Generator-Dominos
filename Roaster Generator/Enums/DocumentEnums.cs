namespace Roaster_Generator.Enums;

/// <summary>The employee group a required-document list applies to.</summary>
public enum DocumentCategory
{
    CarOwn,
    CarCompany,
    MopedOwn,
    MopedCompany,
    EBikeOwn,
    EBikeCompany,
    InStore,
    Manager
}

public enum DocumentRequirementCondition
{
    Always,
    NonEuPassport,
    Stamp2
}

public enum DocumentExpiryRule
{
    None,
    Printed,
    PenaltyPoints
}

public enum DocumentReviewStatus
{
    Clear,
    NeedsReview
}

public enum DocumentAuditAction
{
    Analysed,
    Uploaded,
    Replaced,
    Viewed,
    Downloaded,
    Edited,
    Reviewed,
    Deleted,
    VehicleChanged,
    VehicleCancelled,
    VehicleCompleted
}

public enum VehicleChangeStatus
{
    Open,
    Completed,
    Cancelled
}
