using Microsoft.AspNetCore.Authorization;

namespace TravelTracker.Settings.Api.Access;

/**
 * applies live account standing after token and scope checks: deactivated accounts are denied, banned accounts may read but not write.
 * every endpoint that is not anonymous gets the check, so a new controller cannot skip it.
 * @param next the next request component.
 * @param logger safe refusal diagnostics without identity or credential details.
 */
public sealed class SettingsAccessMiddleware(RequestDelegate next, ILogger<SettingsAccessMiddleware> logger)
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
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata is null || metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("Settings request refused with HTTP 401: the token has no issuer or subject claim.");
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "A validated issuer and subject are required.").ExecuteAsync(context);
            return;
        }

        caller.Issuer = issuer;
        caller.Subject = subject;
        var access = await users.ResolveAsync(issuer, subject, context.RequestAborted);
        var isBannedWrite = access?.Status == "banned" && !HttpMethods.IsGet(context.Request.Method);
        if (access is null || access.Status == "deactivated" || isBannedWrite)
        {
            var status = access is null ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden;
            var reason = access is null ? "live account standing could not be verified" : isBannedWrite ? "banned account tried to write" : "account is deactivated";
            logger.LogWarning("Settings request refused with HTTP {StatusCode}: {Reason}.", status, reason);
            await Results.Problem(statusCode: status,
                title: status == StatusCodes.Status503ServiceUnavailable
                    ? "Account access could not be verified."
                    : "Account is not permitted to perform this operation.").ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
