using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TravelTracker.Accommodations.Api.Errors;

namespace TravelTracker.Accommodations.Api.Tests;

/**
 * Proves that serialization failures and deadlocks reach clients as retryable 503 responses through the production exception pipeline.
 */
public sealed class StorageContentionStatusTests
{
    /**
     * Sends one contention failure through the host's exception pipeline and checks the public outcome.
     * @param sqlState PostgreSQL error code, either a serialization failure or a deadlock.
     * @param wrapped Whether the provider error arrives inside a DbUpdateException.
     * @return Completion after checking the 503 response.
     */
    [Theory]
    [InlineData("40001", false)]
    [InlineData("40P01", false)]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    public async Task ContentionFailure_ReturnsServiceUnavailable(string sqlState, bool wrapped)
    {
        var provider = new PostgresException("secret", "ERROR", "ERROR", sqlState);
        Exception failure = wrapped ? new DbUpdateException("secret", provider) : provider;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = AccommodationExceptionHandler.HandleAsync,
            SuppressDiagnosticsCallback = _ => true
        });
        app.Run(_ => throw failure);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }
}
