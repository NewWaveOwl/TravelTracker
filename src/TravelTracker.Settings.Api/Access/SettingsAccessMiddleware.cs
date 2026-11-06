using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace TravelTracker.Settings.Api.Access;

/**
 * applies live account standing after token and scope checks: deactivated accounts are denied, banned accounts may read but not write.
 * @param next the next request component.
 */
public sealed class SettingsAccessMiddleware(RequestDelegate next)
{
    /**
     * resolves the validated identity against live Users data and fails closed when Users cannot answer.
     * @param context the authenticated HTTP request.
     * @param users the live Users access client.
     * @param caller the per-request validated identity.
     * @return completion after denying the request or invoking the endpoint.
     */
    public async Task InvokeAsync(HttpContext context, UsersAccessClient users, CallerContext caller)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() !=
            typeof(Controllers.SettingsController))
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

        await next(context);
    }
}
