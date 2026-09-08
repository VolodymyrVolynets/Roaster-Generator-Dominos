using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class HolidayRequest
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public int Hours { get; set; }

    public HolidayStatus Status { get; set; } = HolidayStatus.Requested;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }
}
