using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Accommodations.Data.Repositories;
using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Api.Tests.Support;

/** Seeds application-only HTTP test data through the service repository boundary. */
internal static class AccommodationTestData
{
    /**
     * Adds a record for an isolated account without making a protected create request.
     * @param factory Isolated HTTP host and service container.
     * @param name Accommodation name.
     * @param ownerSubject Validated owner subject.
     * @param shared Whether the item belongs to the shared catalogue.
     * @return The public identifier assigned to the fixture.
     */
    internal static async Task<Guid> SeedAsync(ApiFactory factory, string name, string ownerSubject, bool shared)
    {
        var record = new Accommodation
        {
            AccommodationId = Guid.CreateVersion7(),
            Name = name,
            Address = "Test address",
            Latitude = 45.5m,
            Longitude = -73.5m,
            OwnerIssuer = ApiFactory.Issuer,
            OwnerSubject = ownerSubject,
            IsShared = shared
        };
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAccommodationRepository>().AddAsync(record);
        return record.AccommodationId;
    }
}
