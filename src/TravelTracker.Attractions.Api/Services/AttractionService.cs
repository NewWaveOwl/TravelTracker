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
 * @param ids The single-instance application identity allocator.
 * @param caller The validated identity and live Users permission context.
 */
public sealed class AttractionService(IAttractionRepository repository, EntityIdGenerator ids, CallerContext caller) : IAttractionService
{
    /**
     * Reads all entries through the existing contract.
     * @param nameFilter Optional ordinal case-insensitive name substring.
     * @param cancellationToken Cancels database work.
     * @return A task containing the stored entries.
     */
    public async Task<List<Attraction>> GetAllAsync(string? nameFilter = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caller.Issuer) || string.IsNullOrWhiteSpace(caller.Subject)) return [];
        var records = await repository.GetVisibleAsync(caller.Issuer, caller.Subject, !caller.IsBanned, cancellationToken);
        IEnumerable<Attraction> filtered = records;
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var q = nameFilter.Trim();
            filtered = filtered.Where(r => r.Name != null && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        return filtered.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AttractionId.PublicId)
            .ToList();
    }

    /**
     * Reads one saved entry.
     * @param attractionId Identifier to find.
     * @return A task containing the entry, or null when absent.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var record = await repository.GetByIdAsync(attractionId, cancellationToken);
        return record is not null && caller.CanRead(record) ? record : null;
    }

    /**
     * Creates an entity from HTTP-validated values.
     * @param attraction Writable entity values; its supplied identifier is replaced.
     * @return A task containing the stored entity with a generated identifier.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<Attraction> CreateAsync(Attraction attraction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(caller.Subject) || caller.IsBanned || attraction.IsShared && !caller.IsAdministrator)
            throw new UnauthorizedAccessException("Caller cannot create this attraction.");
        attraction.OwnerIssuer = caller.Issuer;
        attraction.OwnerSubject = caller.Subject;
        if (attraction.Validate().Count != 0) throw new ValidationException("Invalid attraction values.");
        attraction.AttractionId = await ids.NextAsync<Attraction>(cancellationToken);
        attraction.Name = attraction.Name.Trim();
        await repository.AddAsync(attraction, cancellationToken);
        return attraction;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param attractionId Identifier of the entity to replace.
     * @param attraction New values supplied by the caller.
     * @return A task containing the entity, or null when absent/private; a visible but unwritable record throws UnauthorizedAccessException.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<Attraction?> UpdateAsync(Guid attractionId, Attraction attraction, CancellationToken cancellationToken = default)
    {
        var result = await WriteAsync(attractionId, _ => new UpdateResult(attraction, null), cancellationToken);
        if (result.Forbidden) throw new UnauthorizedAccessException("Caller cannot edit this attraction.");
        if (result.Errors is not null) throw new ValidationException("Invalid attraction values.");
        return result.Updated;
    }

    /**
     * Removes an attraction through the existing contract.
     * @param attractionId Identifier to remove.
     * @return A task containing whether it was removed.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<bool> DeleteAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var record = await GetByIdAsync(attractionId, cancellationToken);
        return record is not null && caller.CanWrite(record) && await repository.DeleteAsync(attractionId, cancellationToken);
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
        candidate.Name = candidate.Name?.Trim() ?? string.Empty;
        var errors = candidate.Validate();
        if (errors.Count != 0) return new(null, errors);
        await repository.UpdateAsync(candidate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(candidate, null);
    }
}
