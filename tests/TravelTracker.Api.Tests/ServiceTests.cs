using System.Text.Json;
using TravelTracker.Api.Services;
using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Tests;

public sealed class ServiceTests
{
    [Fact]
    public async Task Service_GetAll_SearchTrimCaseInsensitive()
    {
        var repo = new InMemoryAttractionRepository();
        var service = new AttractionService(repo);
        // Ensure query trims and is case-insensitive
        var results = await service.GetAllAsync(" museum ");
        Assert.All(results, r => Assert.Contains("museum", r.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Service_UpdatePartialAsync_Concurrent_AllChangesApplied()
    {
        var repo = new InMemoryAttractionRepository();
        var service = new AttractionService(repo);
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        // Seed a known record
        await repo.AddAsync(new Attraction { AttractionId = id, Name = "Seed", Address = "A", Latitude = 0m, Longitude = 0m, EstimatedVisitMinutes = 10, Description = "d", Rating = 1m });

        var patches = new[]
        {
            JsonDocument.Parse("{ \"name\": \"N1\" }").RootElement,
            JsonDocument.Parse("{ \"description\": \"D1\" }").RootElement,
            JsonDocument.Parse("{ \"rating\": 4 }").RootElement,
            JsonDocument.Parse("{ \"address\": \"Addr\" }").RootElement,
            JsonDocument.Parse("{ \"lat\": 12.34 }").RootElement,
            JsonDocument.Parse("{ \"lon\": -56.78 }").RootElement,
            JsonDocument.Parse("{ \"estimatedVisitMinutes\": 99 }").RootElement
        };

        var tasks = patches.Select(p => service.UpdatePartialAsync(id, p)).ToArray();
        var results = await Task.WhenAll(tasks);
        // All should be successful (not NotFound and no errors)
        Assert.All(results, r => Assert.False(r.NotFound));
        Assert.All(results, r => Assert.Null(r.Errors));

        var final = await repo.GetByIdAsync(id);
        Assert.NotNull(final);
        Assert.Equal("N1", final!.Name);
        Assert.Equal("D1", final.Description);
        Assert.Equal(4m, final.Rating);
        Assert.Equal("Addr", final.Address);
        Assert.Equal(12.34m, final.Latitude);
        Assert.Equal(-56.78m, final.Longitude);
        Assert.Equal(99, final.EstimatedVisitMinutes);
    }
}
