using TravelTracker.Attractions.Api.Access;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using TravelTracker.Attractions.Data;
using TravelTracker.Attractions.Data.Repositories;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Services;

/**
 * Applies attraction rules and server-resolved visibility over durable storage.
 * @param repository The scoped PostgreSQL repository boundary.
 * @param identityGenerator The single-instance application identity allocator.
 * @param caller The validated identity and live Users permission context.
 */
public sealed class AttractionService(IAttractionRepository repository, EntityIdGenerator identityGenerator, CallerContext caller) : IAttractionService
{
    /**
     * Reads the entries visible to the caller, filtered and ordered by the database.
     * @param nameFilter Optional case-insensitive name substring.
     * @param cancellationToken Cancels database work.
     * @return A task containing the visible entries ordered by name.
     */
    public async Task<List<Attraction>> GetAllAsync(string? nameFilter = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caller.Issuer) || string.IsNullOrWhiteSpace(caller.Subject))
            throw new UnauthorizedAccessException("A validated caller identity is required.");
        return await repository.GetVisibleAsync(caller.Issuer, caller.Subject, !caller.IsBanned, nameFilter, cancellationToken);
    }

    /**
     * Reads one saved entry.
     * @param attractionId Identifier to find.
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing the entry, or null when absent or private to someone else.
     */
    public async Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var record = await repository.GetByIdAsync(attractionId, cancellationToken);
        return record is not null && caller.CanRead(record) ? record : null;
    }

    /**
     * Creates an entity from values the HTTP layer has already validated. Name and address are trimmed before storage.
     * @param attraction Writable entity values; its supplied identifier is replaced.
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing the stored entity with a generated identifier.
     * @throws UnauthorizedAccessException When the caller may not create this attraction.
     */
    public async Task<Attraction> CreateAsync(Attraction attraction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(caller.Issuer) || string.IsNullOrEmpty(caller.Subject) || caller.IsBanned ||
            (attraction.IsShared && !caller.IsAdministrator))
            throw new UnauthorizedAccessException("Caller cannot create this attraction.");
        attraction.OwnerIssuer = caller.Issuer;
        attraction.OwnerSubject = caller.Subject;
        attraction.AttractionId = await identityGenerator.NextAsync<Attraction>(cancellationToken);
        attraction.Name = attraction.Name.Trim();
        // a null address is left alone here; the HTTP layer has already rejected it
        if (attraction.Address is not null) attraction.Address = attraction.Address.Trim();
        await repository.AddAsync(attraction, cancellationToken);
        return attraction;
    }

    /**
     * Validates an independent candidate under a database row lock and commits atomically.
     * @param attractionId The public identifier to update.
     * @param patch Presence-aware writable JSON fields.
     * @param cancellationToken Cancels database work and commit.
     * @return The accepted record, field errors, absent/private status or forbidden status.
     */
    public Task<UpdateResult> UpdatePartialAsync(Guid attractionId, JsonElement patch, CancellationToken cancellationToken = default) =>
        WriteAsync(attractionId, existing => AttractionPatchParser.Apply(existing, patch), cancellationToken);

    /**
     * Shares lock, visibility, write authorization, invariant validation and commit for both updates.
     * @param attractionId The public identifier to lock.
     * @param buildCandidate Produces independent values or parsing errors from the visible record.
     * @param cancellationToken Cancels the write session.
     * @return Accepted values, absent/private status, forbidden status or field errors.
     */
    private async Task<UpdateResult> WriteAsync(Guid attractionId, Func<Attraction, UpdateResult> buildCandidate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginWriteAsync(attractionId, cancellationToken);
        var existing = await repository.GetByIdAsync(attractionId, cancellationToken);
        if (existing is null || !caller.CanRead(existing)) return new(null, null, NotFound: true);
        if (!caller.CanWrite(existing)) return new(null, null, Forbidden: true);
        var result = buildCandidate(existing);
        if (result.Errors is not null) return result;
        var candidate = result.Updated!;
        candidate.OwnerIssuer = existing.OwnerIssuer;
        candidate.OwnerSubject = existing.OwnerSubject;
        candidate.IsShared = existing.IsShared;
        candidate.AttractionId = existing.AttractionId;
        candidate.Name = candidate.Name.Trim();
        var errors = candidate.Validate();
        if (errors.Count != 0) return new(null, errors);
        await repository.UpdateAsync(candidate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(candidate, null);
    }
}
