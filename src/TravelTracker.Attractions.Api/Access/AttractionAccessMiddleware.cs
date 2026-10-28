using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace TravelTracker.Attractions.Api.Access;

/**
 * Applies live standing checks after token/scope authorization and before attraction actions.
 * @param next The next request component.
 */
public sealed class AttractionAccessMiddleware(RequestDelegate next)
{
    /**
     * Resolves only the validated identity; failures never permit a catalogue operation.
     * @param context The authenticated HTTP request.
     * @param users The live Users client.
     * @param caller The request-scoped permission context.
     * @return Completion after denial or invoking the action.
     */
    public async Task InvokeAsync(HttpContext context, UsersAccessClient users, CallerContext caller)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() != typeof(Controllers.AttractionsController))
        { await next(context); return; }
        caller.Issuer = context.User.FindFirst("iss")!.Value;
        caller.Subject = context.User.FindFirst("sub")!.Value;
        var access = await users.ResolveAsync(caller.Issuer, caller.Subject, context.RequestAborted);
        if (access is null || access.Status == "deactivated" ||
            access.Status == "banned" && !HttpMethods.IsGet(context.Request.Method))
        {
            var status = access is null ? 503 : 403;
            await Results.Problem(statusCode: status,
                title: status == 503 ? "Account access could not be verified." : "Account is not permitted to perform this operation.")
                .ExecuteAsync(context);
            return;
        }
        caller.IsAdministrator = access.Role == "administrator";
        caller.IsBanned = access.Status == "banned";
        await next(context);
    }
}
