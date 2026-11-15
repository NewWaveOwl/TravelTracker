using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TravelTracker.Api.References;

/**
 * Sends one bounded request to another service and maps its HTTP answer to a reference result. Every
 * non-found outcome is logged with the operation name and a fixed reason. Tokens, URLs, response bodies
 * and exception text are never logged.
 *
 * @param options Supplies the per-call timeout.
 * @param logger Records each failed or absent lookup.
 */
public sealed class ReferenceSender(IOptions<TripReferenceOptions> options, ILogger<ReferenceSender> logger)
{
    /**
     * Sends one request and maps the answer. A 200 with a readable body is Found; 401, 403 and 404 map to their
     * own statuses; every other answer, timeout, transport failure or unreadable body is Unavailable.
     *
     * @param http Typed client whose base address is set.
     * @param request Request to send; it is disposed when this method returns.
     * @param operation Short description of the lookup for logs, such as "accommodation lookup".
     * @param cancellationToken Cancels the caller's operation. Caller cancellation is rethrown, not converted.
     * @return The mapped outcome.
     * @throws OperationCanceledException when the caller's token is cancelled.
     */
    public async Task<ReferenceResult<TBody>> SendAsync<TBody>(HttpClient http, HttpRequestMessage request, string operation, CancellationToken cancellationToken)
        where TBody : class
    {
        var timeout = options.Value.Timeout;
        using var owned = request;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(owned, deadline.Token);
            return response.StatusCode == HttpStatusCode.OK
                ? await ReadBodyAsync<TBody>(response, operation, deadline.Token)
                : Unsuccessful<TBody>(response.StatusCode, operation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("{Operation} timed out after {Seconds} s; treated as unavailable.", operation, timeout.TotalSeconds);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream did not answer within the timeout.");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("{Operation} could not reach its service; treated as unavailable.", operation);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream could not be reached.");
        }
        catch (IOException)
        {
            logger.LogWarning("{Operation} lost its connection while reading the answer; treated as unavailable.", operation);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream connection failed while reading the answer.");
        }
        catch (JsonException)
        {
            logger.LogWarning("{Operation} returned a body that is not valid JSON; treated as unavailable.", operation);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream returned a body that is not valid JSON.");
        }
        catch (NotSupportedException)
        {
            // ReadFromJsonAsync throws this when the content type is not JSON.
            logger.LogWarning("{Operation} returned a content type that is not JSON; treated as unavailable.", operation);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream returned a content type that is not JSON.");
        }
        catch (InvalidOperationException exception) when (exception.InnerException is ArgumentException)
        {
            // an unreadable response charset surfaces as InvalidOperationException wrapping ArgumentException.
            logger.LogWarning("{Operation} returned a charset that could not be read; treated as unavailable.", operation);
            return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream returned a charset that could not be read.");
        }
    }

    /**
     * Maps a non-200 answer to its outcome and logs it at the level the outcome deserves.
     *
     * @param status The HTTP status the upstream returned.
     * @param operation The lookup description used in the log line.
     * @return The mapped failure.
     */
    private ReferenceResult<TBody> Unsuccessful<TBody>(HttpStatusCode status, string operation) where TBody : class
    {
        switch (status)
        {
            case HttpStatusCode.Unauthorized:
                logger.LogWarning("{Operation} was rejected: upstream answered HTTP 401.", operation);
                return ReferenceResult<TBody>.Failed(ReferenceStatus.Unauthorized, "upstream rejected the credential.");
            case HttpStatusCode.Forbidden:
                logger.LogWarning("{Operation} was refused: upstream answered HTTP 403.", operation);
                return ReferenceResult<TBody>.Failed(ReferenceStatus.Forbidden, "upstream refused the request for this caller.");
            case HttpStatusCode.NotFound:
                // a wrong base address also answers 404, so the log says both readings.
                logger.LogWarning("{Operation} found no visible record: upstream answered HTTP 404, which is also what a wrong base address returns.", operation);
                return ReferenceResult<TBody>.Failed(ReferenceStatus.NotFound, "upstream has no visible record for this request.");
            default:
                logger.LogWarning("{Operation} failed: upstream answered HTTP {StatusCode}; treated as unavailable.", operation, (int)status);
                return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, $"upstream answered HTTP {(int)status}.");
        }
    }

    /**
     * Checks that the place fields of a 200 answer are all present and usable as a snapshot.
     *
     * @param name The place name.
     * @param address The place address.
     * @param latitude The latitude in degrees.
     * @param longitude The longitude in degrees.
     * @return True when name and address are not blank and the coordinates are inside -90..90 and -180..180.
     */
    internal static bool IsUsablePlace([NotNullWhen(true)] string? name, [NotNullWhen(true)] string? address,
        [NotNullWhen(true)] decimal? latitude, [NotNullWhen(true)] decimal? longitude) =>
        !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(address)
        && latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m;

    /**
     * Reports a 200 answer that lacked the fields a snapshot needs. Defaulting them would save a wrong place,
     * for example coordinates 0, 0, so the lookup fails closed like any other unreadable answer.
     *
     * @param operation The lookup description used in the log line.
     * @return An Unavailable result without a value.
     */
    internal ReferenceResult<TValue> Incomplete<TValue>(string operation)
    {
        logger.LogWarning("{Operation} answered without the fields a snapshot needs; treated as unavailable.", operation);
        return ReferenceResult<TValue>.Failed(ReferenceStatus.Unavailable, "upstream response was missing required fields.");
    }

    /**
     * Reads a successful body, treating an empty body as unavailable.
     *
     * @param response The 200 response to read.
     * @param operation The lookup description used in the log line.
     * @param cancellationToken Cancels the read.
     * @return Found with the body, or Unavailable when the body is empty.
     */
    private async Task<ReferenceResult<TBody>> ReadBodyAsync<TBody>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
        where TBody : class
    {
        var body = await response.Content.ReadFromJsonAsync<TBody>(cancellationToken);
        if (body is not null) return ReferenceResult<TBody>.Found(body);
        logger.LogWarning("{Operation} returned an empty body; treated as unavailable.", operation);
        return ReferenceResult<TBody>.Failed(ReferenceStatus.Unavailable, "upstream returned an empty body.");
    }
}
