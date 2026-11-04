using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TravelTracker.Attractions.Data;
using TravelTracker.Attractions.Data.Repositories;
using TravelTracker.Attractions.Data.SeedData;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Initializes only the isolated test InMemory provider, never migrations or a production database.
 * @param scopes The test host's scope factory.
 * @param seedExamples Whether the test requests shared examples.
 */
internal sealed class InMemoryAttractionsInitializer(IServiceScopeFactory scopes, bool seedExamples) : IHostedService
{
    /**
     * Creates an isolated EF InMemory store and optionally populates shared examples.
     * @param cancellationToken Cancels test initialization.
     * @return Completion after seeding; no migration or relational guarantee is involved.
     */
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AttractionsDbContext>();
        await database.Database.EnsureCreatedAsync(cancellationToken);
        if (!seedExamples) return;
        var repository = scope.ServiceProvider.GetRequiredService<IAttractionRepository>();
        foreach (var attraction in AttractionSeedData.Create())
        {
            attraction.IsShared = true;
            await repository.AddAsync(attraction, cancellationToken);
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
