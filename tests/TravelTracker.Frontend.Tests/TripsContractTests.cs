using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** The HTTP-boundary pieces the trip views rely on: which paths may carry a token and which field errors survive. */
public sealed class TripsContractTests
{
    static readonly ServiceDirectory Directory = new(new Dictionary<string, string>
    {
        ["Trips"] = "http://localhost:5108", ["Accommodations"] = "http://localhost:5106", ["Attractions"] = "http://localhost:5104",
    });

    [Fact]
    public void AllowsTripAndAccommodationPathsOnTheirOwnServices()
    {
        Assert.Equal("http://localhost:5108/api/v1/trips", Directory.ProtectedUrlFor("Trips", "/api/v1/trips"));
        var id = Guid.NewGuid();
        Assert.Equal($"http://localhost:5108/api/v1/trips/{id:D}", Directory.ProtectedUrlFor("Trips", $"/api/v1/trips/{id:D}"));
        Assert.Equal("http://localhost:5106/api/v1/accommodations?name=Hotel", Directory.ProtectedUrlFor("Accommodations", "/api/v1/accommodations?name=Hotel"));
    }

    [Theory]
    [InlineData("Trips", "/api/v1/trips?owner=someone")]
    [InlineData("Trips", "/api/v1/trips?name=x")]
    [InlineData("Accommodations", "/api/v1/accommodations?owner=someone")]
    [InlineData("Accommodations", "/api/v1/attractions?name=x")]
    public void RefusesQueriesThatAreNotPartOfTheContract(string service, string path) =>
        Assert.Equal("Unsupported API path.", Assert.Throws<ApiError>(() => Directory.ProtectedUrlFor(service, path)).Message);

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task KeepsLegFieldErrorsFromTheTripsService(int status)
    {
        var body = """{"errors":{"legs[1].arrival":["arrival must be after departure."],"legs":["a trip needs at least one leg."],"name":["name is required."],"password":["ignored"]}}""";
        var client = new ServiceClient(new HttpClient(StubHandler.Status(status, body)), NullLogger<ServiceClient>.Instance);
        var error = await Assert.ThrowsAsync<ApiError>(() => client.SendJsonAsync<object>(HttpMethod.Post, "http://localhost:5108/api/v1/trips", new { }));
        Assert.Equal(["legs", "legs[1].arrival", "name"], error.Errors.Keys.Order());
        Assert.Equal("arrival must be after departure.", error.Errors["legs[1].arrival"].Single());
    }
}
