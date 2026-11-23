using Microsoft.AspNetCore.Components.Authorization;

namespace TravelTracker.Frontend.Api.TicketImports;

/** Downloads private source PDFs through the signed-in frontend, without putting access tokens in URLs. */
public static class TicketDocumentEndpoint
{
    /**
     * Maps the authenticated source-document download proxy.
     * @param app Frontend endpoint builder.
     */
    public static void MapTicketDocument(this WebApplication app) =>
        app.MapGet("/ticket-documents/{tripId:guid}/{importId:guid}", DownloadAsync);

    /**
     * Revalidates the session and backend ownership before returning a non-cacheable attachment.
     * @param context Current authenticated HTTP request.
     * @param tripId Public trip identifier.
     * @param importId Temporary document identifier.
     * @param authentication Request-scoped frontend authentication provider.
     * @param client Protected owner-scoped Trips client.
     * @return The PDF attachment, or a safe status without service diagnostics.
     */
    static async Task<IResult> DownloadAsync(HttpContext context, Guid tripId, Guid importId,
        AuthenticationStateProvider authentication, TicketImportsClient client)
    {
        context.Response.Headers.CacheControl = "no-store, private";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; frame-ancestors 'none'";
        if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
        if (authentication is IHostEnvironmentAuthenticationStateProvider host)
            host.SetAuthenticationState(Task.FromResult(new AuthenticationState(context.User)));
        try
        {
            var bytes = await client.GetDocumentAsync(tripId, importId, context.RequestAborted);
            context.Response.OnCompleted(() => { Array.Clear(bytes); return Task.CompletedTask; });
            return Results.File(bytes, "application/pdf", "ticket.pdf", enableRangeProcessing: false);
        }
        catch (ApiError error)
        {
            return Results.Problem(statusCode: error.Status is 401 or 403 or 404 ? error.Status : 503,
                title: "Ticket PDF unavailable", detail: error.Message);
        }
    }
}
