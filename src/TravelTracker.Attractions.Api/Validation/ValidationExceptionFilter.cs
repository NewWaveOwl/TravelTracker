using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace TravelTracker.Attractions.Api.Validation;

/**
 * Maps application field failures to standard HTTP validation problem responses.
 */
public sealed class ValidationExceptionFilter : IExceptionFilter
{
    /**
     * Handles expected validation failures without exposing internal exception details.
     * @param context The current HTTP exception context.
     */
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InputValidationException validation) return;
        var result = new BadRequestObjectResult(new ValidationProblemDetails(validation.Errors)
            { Status = 400, Title = "One or more validation errors occurred." });
        result.ContentTypes.Add("application/problem+json");
        context.Result = result;
        context.ExceptionHandled = true;
    }
}
