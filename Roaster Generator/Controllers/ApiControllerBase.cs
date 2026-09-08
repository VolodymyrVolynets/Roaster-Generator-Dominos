using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace Roaster_Generator.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected async Task<IActionResult?> ValidateRequestAsync<TRequest>(
        IValidator<TRequest> validator,
        TRequest request,
        string title,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid ? null : ValidationError(ToErrors(result), title);
    }

    protected IActionResult ValidationError(
        IDictionary<string, string[]> errors,
        string title) =>
        new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Title = title
        });

    protected static Dictionary<string, string[]> ToErrors(ValidationResult result) =>
        result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());
}
