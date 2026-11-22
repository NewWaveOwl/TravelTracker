using Microsoft.AspNetCore.Builder;
using TravelTracker.Security.Hosting;

namespace TravelTracker.Users.Tests;

/** Exercises the connection validation shared by all API entry points. */
public sealed class DatabaseConfigurationTests
{
    [Theory]
    [InlineData(null, "is required")]
    [InlineData("Host=localhost;Password=test-secret-sentinel;BadOption=true", "valid PostgreSQL")]
    [InlineData("Host=localhost;Password=test-secret-sentinel;Log Parameters=true", "Log Parameters")]
    [InlineData("Host=localhost;Password=test-secret-sentinel;Include Error Detail=true", "Include Error Detail")]
    [InlineData("Host=localhost;Database=users;Username=runtime;Password=test-secret-sentinel;SSL Mode=Require", "VerifyFull")]
    public void RejectsUnsafeProductionSettingsWithoutEchoingValues(string? connection, string reason)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
        builder.Configuration["ConnectionStrings:Users"] = connection;

        var error = Assert.Throws<InvalidOperationException>(() => builder.RequireDatabaseConnection("Users"));
        Assert.Contains("ConnectionStrings:Users", error.Message);
        Assert.Contains(reason, error.Message);
        Assert.DoesNotContain("test-secret-sentinel", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("Production", "Host=db.example.test;Database=users;Username=runtime;SSL Mode=VerifyFull")]
    [InlineData("Development", "Host=localhost;Database=users;SSL Mode=Disable")]
    public void AcceptsVerifiedProductionAndIsolatedDevelopmentSettings(string environment, string connection)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, Args = [] });
        builder.Configuration["ConnectionStrings:Users"] = connection;

        Assert.Equal(connection, builder.RequireDatabaseConnection("Users"));
    }

    [Fact]
    public async Task MigrationFailureKeepsOnlyTheFailureType()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseConfiguration.RunMigrationAsync(() => Task.FromException(new IOException("test-secret-sentinel"))));

        Assert.Contains(nameof(IOException), error.Message);
        Assert.DoesNotContain("test-secret-sentinel", error.ToString());
        Assert.Null(error.InnerException);
    }
}
