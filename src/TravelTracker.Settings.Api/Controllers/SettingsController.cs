using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Authentication;
using TravelTracker.Settings.Api.Contracts;
using TravelTracker.Settings.Api.Services;
using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Api.Controllers;

/**
 * exposes the caller's own preferences. there is no user identifier in any route or body; ownership comes from the validated token.
 * every response carries the saved revision as a strong ETag, and a replace must echo it in If-Match.
 * @param service the request-scoped settings application service.
 */
[ApiController]
[Route("api/v1/settings")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class SettingsController(ISettingsService service) : ControllerBase
{
    /**
     * reads the caller's preferences, returning the documented defaults (ETag "0") before the first save.
     * @return 200 with the current preferences and ETag.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.SettingsRead)]
    [ProducesResponseType(typeof(SettingsRecord), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsRecord>> Get()
    {
        var settings = await service.GetAsync(HttpContext.RequestAborted);
        Response.Headers.ETag = ToEntityTag(settings.Revision);
        return Ok(ToResponse(settings));
    }

    /**
     * replaces every preference the caller owns, but only when If-Match names the current revision.
     * order: body shape (400), missing If-Match (428), field validation (400), stale or malformed If-Match (412).
     * @param request the complete writable preference set.
     * @param ifMatch the ETag the caller last read, for example "3".
     * @return 200 with the saved preferences and new ETag; 400 field errors; 412 when the settings changed since they were read.
     */
    [HttpPut]
    [Authorize(Policy = ScopePolicies.SettingsWrite)]
    [ProducesResponseType(typeof(SettingsRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<SettingsRecord>> Replace(UpdateSettingsRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
            return Problem(statusCode: StatusCodes.Status428PreconditionRequired,
                title: "An If-Match header naming the settings ETag is required to replace settings.");

        var result = await service.ReplaceAsync(new UserSettings
        {
            Theme = request.Theme!,
            DefaultMapLatitude = request.DefaultMapLatitude!.Value,
            DefaultMapLongitude = request.DefaultMapLongitude!.Value,
            DefaultMapZoom = request.DefaultMapZoom!.Value,
            PreferredTransportMode = request.PreferredTransportMode!,
            TimeZone = request.TimeZone!
        }, ParseEntityTag(ifMatch) ?? -1, HttpContext.RequestAborted);
        if (result.Errors is not null) return ValidationProblem(new ValidationProblemDetails(result.Errors));
        if (result.Stale)
            return Problem(statusCode: StatusCodes.Status412PreconditionFailed,
                title: "The settings changed since they were read. Reload them, review the changes and save again.");
        Response.Headers.ETag = ToEntityTag(result.Saved!.Revision);
        return Ok(ToResponse(result.Saved!));
    }

    /**
     * formats a revision as a strong entity tag.
     * @param revision the stored revision.
     * @return the quoted tag, for example "3".
     */
    private static string ToEntityTag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    /**
     * reads a strong entity tag back into its revision. weak tags, wildcards and malformed values yield null, which never matches.
     * @param value the raw If-Match value.
     * @return the revision, or null when the value is not a strong tag of a non-negative revision.
     */
    private static long? ParseEntityTag(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '"' || trimmed[^1] != '"') return null;
        return long.TryParse(trimmed[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) && revision >= 0
            ? revision
            : null;
    }

    /**
     * maps domain preferences to the public wire shape.
     * @param settings the preference values.
     * @return the public response record.
     */
    private static SettingsRecord ToResponse(UserSettings settings) => new(settings.Theme,
        settings.DefaultMapLatitude, settings.DefaultMapLongitude, settings.DefaultMapZoom,
        settings.PreferredTransportMode, settings.TimeZone);
}
