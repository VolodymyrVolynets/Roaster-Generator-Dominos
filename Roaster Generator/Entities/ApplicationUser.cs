using Microsoft.AspNetCore.Identity;

namespace Roaster_Generator.Entities;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool HasChangedPassword { get; set; }

    public Guid? EmployeeId { get; set; }

    public Employee? Employee { get; set; }
}
