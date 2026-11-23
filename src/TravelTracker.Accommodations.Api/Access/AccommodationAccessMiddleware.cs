using Microsoft.AspNetCore.Authorization;

namespace TravelTracker.Accommodations.Api.Access;

/**
 * Applies live account standing before accommodation actions: deactivated accounts are denied, and banned accounts see only their own private records and cannot write.
 * Every endpoint that is not anonymous gets the check, so a new controller cannot skip it.
 * @param next The next request component.
 * @param logger Safe refusal diagnostics without identity or credential details.
 */
public sealed class AccommodationAccessMiddleware(RequestDelegate next, ILogger<AccommodationAccessMiddleware> logger)
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
            logger.LogWarning("Accommodations request refused with HTTP 401: the token has no issuer or subject claim.");
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
            logger.LogWarning("Accommodations request refused with HTTP {StatusCode}: {Reason}.", status, reason);
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
