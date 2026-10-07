using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace TravelTracker.Transport.Api.Validation;
/**
 * Maps expected application failures to public problem responses.
 */
public sealed class ValidationExceptionFilter : IExceptionFilter
{
    /**
     * Maps expected application failures to HTTP problems.
     * @param context Current request failure.
     */
    public void OnException(ExceptionContext context)
    {
        ObjectResult? result = context.Exception switch
        {
            InputValidationException invalid => new BadRequestObjectResult(
                new ValidationProblemDetails(invalid.Errors) { Status = 400, Title = "One or more validation errors occurred." }),
            ResourceNotFoundException missing => new NotFoundObjectResult(
                new ProblemDetails { Status = 404, Title = missing.Message }),
            _ => null
        };
        if (result is null) return;
        result.ContentTypes.Add("application/problem+json");
        context.Result = result;
        context.ExceptionHandled = true;
    }
}
