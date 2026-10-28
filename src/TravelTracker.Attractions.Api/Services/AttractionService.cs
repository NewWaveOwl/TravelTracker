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
        var records = await repository.GetVisibleAsync(caller.Issuer, caller.Subject, !caller.IsBanned, cancellationToken);
        IEnumerable<Attraction> filtered = records.Where(caller.CanRead);
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
     * @return A task containing the entity, or null when absent; no HTTP update endpoint is exposed.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<Attraction?> UpdateAsync(Guid attractionId, Attraction attraction, CancellationToken cancellationToken = default)
    {
        await using var transaction = await repository.BeginWriteAsync(attractionId, cancellationToken);
        var existing = await GetByIdAsync(attractionId, cancellationToken);
        if (existing is null || !caller.CanWrite(existing)) return null;
        if (attraction.Validate().Count != 0) throw new ValidationException("Invalid attraction values.");
        attraction.OwnerIssuer = existing.OwnerIssuer;
        attraction.OwnerSubject = existing.OwnerSubject;
        attraction.IsShared = existing.IsShared;
        // Preserve the existing internal/public identity when replacing values.
        attraction.AttractionId = existing.AttractionId;
        attraction.Name = attraction.Name.Trim();
        await repository.UpdateAsync(attraction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return attraction;
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
    public async Task<UpdateResult> UpdatePartialAsync(Guid attractionId, JsonElement patch, CancellationToken cancellationToken = default)
    {
        await using var transaction = await repository.BeginWriteAsync(attractionId, cancellationToken);
        {
            var existing = await repository.GetByIdAsync(attractionId, cancellationToken);
            if (existing is null || !caller.CanRead(existing)) return new UpdateResult(null, null, NotFound: true);
            if (!caller.CanWrite(existing)) return new UpdateResult(null, null, Forbidden: true);
            // Patch must be an object
            if (patch.ValueKind != JsonValueKind.Object)
            {
                return new UpdateResult(null, new Dictionary<string, string[]> { ["patch"] = new[] { "Request body must be a JSON object." } });
            }

            // Collect provided properties and detect unknowns
            var allowed = new HashSet<string>(new[] { "name", "description", "rating", "address", "lat", "lon", "estimatedVisitMinutes" }, StringComparer.OrdinalIgnoreCase);
            var provided = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in patch.EnumerateObject())
            {
                if (!allowed.Contains(prop.Name))
                {
                    return new UpdateResult(null, new Dictionary<string, string[]> { [prop.Name] = new[] { "Unknown field." } });
                }
                provided[prop.Name] = prop.Value;
            }

            if (provided.Count == 0)
                return new UpdateResult(null, new Dictionary<string, string[]> { ["patch"] = new[] { "Empty update object is not allowed." } });

            // Start from a copy
            var candidate = new Attraction
            {
                OwnerIssuer = existing.OwnerIssuer, OwnerSubject = existing.OwnerSubject, IsShared = existing.IsShared,
                AttractionId = existing.AttractionId,
                Name = existing.Name,
                Description = existing.Description,
                Rating = existing.Rating,
                Address = existing.Address,
                Latitude = existing.Latitude,
                Longitude = existing.Longitude,
                EstimatedVisitMinutes = existing.EstimatedVisitMinutes
            };

            var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void AddError(string field, string message)
            {
                if (!errors.TryGetValue(field, out var list)) { list = new List<string>(); errors[field] = list; }
                list.Add(message);
            }

            // Apply provided fields with validation rules per specification
            foreach (var kv in provided)
            {
                var name = kv.Key;
                var value = kv.Value;
                try
                {
                    switch (name.ToLowerInvariant())
                    {
                        case "name":
                            if (value.ValueKind == JsonValueKind.Null) { AddError("name", "Name cannot be null."); break; }
                            var nm = value.GetString() ?? string.Empty;
                            nm = nm.Trim();
                            if (string.IsNullOrEmpty(nm)) AddError("name", "Name must not be empty.");
                            else candidate.Name = nm;
                            break;
                        case "description":
                            if (value.ValueKind == JsonValueKind.Null) candidate.Description = null;
                            else candidate.Description = value.GetString();
                            break;
                        case "rating":
                            if (value.ValueKind == JsonValueKind.Null) { candidate.Rating = null; }
                            else
                            {
                                if (value.TryGetDecimal(out var dec))
                                {
                                    if (dec < 0m || dec > 5m) AddError("rating", "Rating must be between 0 and 5.");
                                    else candidate.Rating = dec;
                                }
                                else AddError("rating", "Rating must be a number.");
                            }
                            break;
                        case "address":
                            if (value.ValueKind == JsonValueKind.Null) { AddError("address", "Address cannot be null."); break; }
                            candidate.Address = value.GetString() ?? string.Empty;
                            break;
                        case "lat":
                            if (value.ValueKind == JsonValueKind.Null) { AddError("lat", "Latitude cannot be null."); break; }
                            if (value.TryGetDecimal(out var lat))
                            {
                                if (lat < -90m || lat > 90m) AddError("lat", "Latitude must be between -90 and 90.");
                                else candidate.Latitude = lat;
                            }
                            else AddError("lat", "Latitude must be a number.");
                            break;
                        case "lon":
                            if (value.ValueKind == JsonValueKind.Null) { AddError("lon", "Longitude cannot be null."); break; }
                            if (value.TryGetDecimal(out var lon))
                            {
                                if (lon < -180m || lon > 180m) AddError("lon", "Longitude must be between -180 and 180.");
                                else candidate.Longitude = lon;
                            }
                            else AddError("lon", "Longitude must be a number.");
                            break;
                        case "estimatedvisitminutes":
                            if (value.ValueKind == JsonValueKind.Null) { AddError("estimatedVisitMinutes", "EstimatedVisitMinutes cannot be null."); break; }
                            if (value.TryGetInt32(out var mins))
                            {
                                if (mins < 0) AddError("estimatedVisitMinutes", "EstimatedVisitMinutes must be non-negative.");
                                else candidate.EstimatedVisitMinutes = mins;
                            }
                            else AddError("estimatedVisitMinutes", "EstimatedVisitMinutes must be an integer.");
                            break;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or FormatException)
                {
                    AddError(name, "Invalid value type.");
                }
            }

            if (errors.Count > 0)
            {
                var dict = errors.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
                return new UpdateResult(null, new Dictionary<string, string[]>(dict, StringComparer.OrdinalIgnoreCase));
            }

            // Trim candidate name
            candidate.Name = candidate.Name?.Trim() ?? string.Empty;

            var invariantErrors = candidate.Validate();
            if (invariantErrors.Count != 0) return new UpdateResult(null, invariantErrors);

            await repository.UpdateAsync(candidate, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new UpdateResult(candidate, null, NotFound: false);
        }
    }

}
