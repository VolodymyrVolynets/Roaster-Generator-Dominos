namespace Roaster_Generator.Entities;

public sealed class Shift
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly FinishTime { get; set; }

    public Employee Employee { get; set; } = null!;
}
