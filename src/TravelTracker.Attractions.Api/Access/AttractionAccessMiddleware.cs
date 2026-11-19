using Microsoft.AspNetCore.Authorization;

namespace TravelTracker.Attractions.Api.Access;

/**
 * Applies live standing checks after token/scope authorization and before attraction actions.
 * @param next The next request component.
 * @param logger Safe refusal diagnostics without identity or credential details.
 */
public sealed class AttractionAccessMiddleware(RequestDelegate next, ILogger<AttractionAccessMiddleware> logger)
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
        // fail closed: only unmatched requests and explicitly anonymous endpoints skip the live standing check
        var endpoint = context.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("Attractions request refused with HTTP 401: the token has no issuer or subject claim.");
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "A validated issuer and subject are required.").ExecuteAsync(context);
            return;
        }
        caller.Issuer = issuer;
        caller.Subject = subject;
        var access = await users.ResolveAsync(caller.Issuer, caller.Subject, context.RequestAborted);
        if (access is null || access.Status == "deactivated" ||
            (access.Status == "banned" && !HttpMethods.IsGet(context.Request.Method)))
        {
            var status = access is null ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden;
            var reason = access is null ? "live account standing could not be verified" : access.Status == "deactivated" ? "account is deactivated" : "banned account tried to write";
            logger.LogWarning("Attractions request refused with HTTP {StatusCode}: {Reason}.", status, reason);
            await Results.Problem(statusCode: status,
                title: status == StatusCodes.Status503ServiceUnavailable
                    ? "Account access could not be verified."
                    : "Account is not permitted to perform this operation.")
                .ExecuteAsync(context);
            return;
        }
        caller.IsAdministrator = access.Role == "administrator";
        caller.IsBanned = access.Status == "banned";
        await next(context);
    }
}
