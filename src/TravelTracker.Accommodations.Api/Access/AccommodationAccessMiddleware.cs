using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace TravelTracker.Accommodations.Api.Access;

/** Applies live account standing before accommodation catalogue actions. */
public sealed class AccommodationAccessMiddleware(RequestDelegate next)
{
    /**
     * Resolves only the validated identity and fails closed when Users is unavailable.
     * @param context The authenticated HTTP request.
     * @param users The live Users access client.
     * @param caller The per-request validated identity and permissions.
     * @return Completion after denying the operation or invoking the endpoint.
     */
    public async Task InvokeAsync(HttpContext context, UsersAccessClient users, CallerContext caller)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() !=
            typeof(Controllers.AccommodationsController))
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "A validated issuer and subject are required.").ExecuteAsync(context);
            return;
        }

        caller.Issuer = issuer;
        caller.Subject = subject;
        var access = await users.ResolveAsync(issuer, subject, context.RequestAborted);
        if (access is null || access.Status == "deactivated" ||
            access.Status == "banned" && !HttpMethods.IsGet(context.Request.Method))
        {
            var status = access is null ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden;
            await Results.Problem(statusCode: status,
                title: status == StatusCodes.Status503ServiceUnavailable
                    ? "Account access could not be verified."
                    : "Account is not permitted to perform this operation.").ExecuteAsync(context);
            return;
        }

        caller.IsAdministrator = access.Role == "administrator";
        caller.IsBanned = access.Status == "banned";
        await next(context);
    }
}
