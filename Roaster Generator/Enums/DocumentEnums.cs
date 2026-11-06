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
    Signed,
    ManagerSigned,
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

public enum DocumentKind
{
    Upload,
    Sign
}

/// <summary>Where the value of a field on a signed form comes from.</summary>
public enum DocumentFieldSource
{
    Ask,
    FullName,
    FirstName,
    LastName,
    EmployeeNumber,
    PhoneNumber,
    PayrollNumber,
    VehicleRegistration,
    PenaltyPoints,
    Today,
    StoreId,
    StoreName,
    Signature,
    Ignore,
    ManagerName,
    ManagerSignature,
    ManagerDate,
    FranchiseeName,
    AlwaysTicked,
    TickedForMoped
}
