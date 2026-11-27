using TravelTracker.Transport.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace TravelTracker.Transport.Api.Access;

/** Applies live account standing and administrator checks to shared Transport catalogue routes. */
public sealed class TransportAccessMiddleware(RequestDelegate next)
{
    /**
     * Requires live active standing. Shared mode creation additionally requires administrator privileges.
     * @param context The authenticated HTTP request.
     * @param users The live Users access client.
     * @return Completion after rejecting the request or invoking its endpoint.
     */
    public async Task InvokeAsync(HttpContext context, UsersAccessClient users)
    {
        var endpoint = context.GetEndpoint();
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null ||
            endpoint!.Metadata.GetMetadata<IAllowAnonymous>() is not null)
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

        var access = await users.ResolveAsync(issuer, subject, context.RequestAborted);
        if (access is null)
        {
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account access could not be verified.").ExecuteAsync(context);
            return;
        }

        var isRead = HttpMethods.IsGet(context.Request.Method);
        var isWrite = HttpMethods.IsPost(context.Request.Method);
        var routePoint = action.ControllerTypeInfo.AsType() == typeof(RoutePointsController);
        var allowed = access.Status == "active" &&
            (isRead || isWrite && (routePoint || access.Role == "administrator"));
        if (!allowed)
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Account is not permitted to perform this operation.").ExecuteAsync(context);
            return;
        }

        context.Features.Set(access);
        await next(context);
    }
}
