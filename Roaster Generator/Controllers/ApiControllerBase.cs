using Microsoft.AspNetCore.Mvc;

namespace Roaster_Generator.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult ValidationError(
        IDictionary<string, string[]> errors,
        string title) =>
        new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Title = title
        });
}
