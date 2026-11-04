using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Attractions.Api.Access;
using TravelTracker.Attractions.Api.Controllers;
using TravelTracker.Attractions.Api.Errors;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Proves safe rejection and diagnostics at the identity, dependency and exception boundaries.
 */
public sealed class FailureBoundaryTests
{
    [Theory]
    [InlineData("https://issuer.example.test", null)]
    [InlineData(null, "alice")]
    [InlineData("https://issuer.example.test", " ")]
    [InlineData(" ", "alice")]
    public async Task MissingIdentity_IsUnauthorizedBeforeUsersLookup(string? issuer, string? subject)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        await using var provider = services;
        context.RequestServices = provider;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim>();
        if (issuer is not null) claims.Add(new Claim("iss", issuer));
        if (subject is not null) claims.Add(new Claim("sub", subject));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerTypeInfo = typeof(AttractionsController).GetTypeInfo() }), "attractions"));
        using var capture = new AuthenticationLogCapture();
        using var http = new HttpClient(new FailureHandler("unexpected"));
        var users = new UsersAccessClient(http, new ConfigurationBuilder().Build(), new Logger<UsersAccessClient>(new LoggerFactory([capture])));
        var called = false;
        await new AttractionAccessMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context, users, new CallerContext());
        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(called);
        Assert.Empty(capture.Messages);
    }

    [Theory]
    [InlineData("missing-endpoint", "HTTPS endpoint")]
    [InlineData("http-endpoint", "HTTPS endpoint")]
    [InlineData("missing-credential", "service credential")]
    [InlineData("credential", "credential format")]
    [InlineData("timeout", "timeout")]
    [InlineData("transport", "transport failure")]
    [InlineData("http", "HTTP 503")]
    [InlineData("not-found", "HTTP 404")]
    [InlineData("unexpected-success", "HTTP 201")]
    [InlineData("json", "invalid JSON")]
    [InlineData("status", "invalid status or role")]
    [InlineData("role", "invalid status or role")]
    [InlineData("null", "invalid status or role")]
    [InlineData("encoding", "invalid response encoding")]
    public async Task UsersFailures_LogDistinctSafeReasonAndFailClosed(string failure, string reason)
    {
        using var capture = new AuthenticationLogCapture();
        using var logs = new LoggerFactory([capture]);
        using var http = new HttpClient(new FailureHandler(failure));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Users:AccessEndpoint"] = failure == "missing-endpoint" ? null : failure == "http-endpoint" ? "http://secret-host.test" : "https://secret-host.test",
            ["Users:ServiceAccessToken"] = failure == "missing-credential" ? null : failure == "credential" ? "secret\ncredential" : "secret-credential"
        }).Build();
        var users = new UsersAccessClient(http, configuration, logs.CreateLogger<UsersAccessClient>());
        Assert.Null(await users.ResolveAsync("secret-issuer", "secret-subject", CancellationToken.None));
        var message = Assert.Single(capture.Messages);
        Assert.Contains(reason, message);
        Assert.DoesNotContain("secret", message);
        Assert.Empty(capture.Exceptions);
    }

    [Theory]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    public async Task SignedTokenWithoutSubject_IsUnauthorizedForAttractions(string failure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(failure: failure));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Users!.Calls);
    }

    [Fact]
    public async Task UsersCallerCancellation_PropagatesWithoutOutageLog()
    {
        using var capture = new AuthenticationLogCapture();
        using var logs = new LoggerFactory([capture]);
        using var http = new HttpClient(new FailureHandler("timeout"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Users:AccessEndpoint"] = "https://users.test", ["Users:ServiceAccessToken"] = "test" }).Build();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UsersAccessClient(http, config, logs.CreateLogger<UsersAccessClient>()).ResolveAsync("issuer", "subject", cancellation.Token));
        Assert.Empty(capture.Messages);
    }

    [Theory]
    [InlineData("unique", 409)]
    [InlineData("check", 400)]
    [InlineData("wrapped-unique", 409)]
    [InlineData("wrapped-check", 400)]
    [InlineData("unavailable", 503)]
    [InlineData("wrapped-unavailable", 503)]
    [InlineData("unknown-database", 500)]
    [InlineData("forbidden", 403)]
    [InlineData("validation", 400)]
    [InlineData("cancel", 408)]
    [InlineData("unknown", 500)]
    [InlineData("serialization", 409)]
    [InlineData("deadlock", 409)]
    [InlineData("wrapped-serialization", 409)]
    [InlineData("wrapped-deadlock", 409)]
    [InlineData("concurrency", 409)]
    public async Task ExceptionPipeline_ReturnsSafeNonRetryableOrTransientOutcome(string failure, int status)
    {
        using var capture = new AuthenticationLogCapture();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders().AddProvider(capture);
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseAttractionExceptionHandling();
        app.Run(_ => throw CreateFailure(failure));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        Assert.DoesNotContain("secret", body);
        Assert.Contains(capture.Messages, message => message.Contains($"HTTP {status}"));
        Assert.DoesNotContain(capture.Messages, message => message.Contains("secret"));
        Assert.Empty(capture.Exceptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPipeline_AbortedRequestLogsWarningWithoutRawExceptionOrProblemBody(bool ioFailure)
    {
        using var capture = new AuthenticationLogCapture();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders().AddProvider(capture);
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseAttractionExceptionHandling();
        app.Run(context =>
        {
            context.RequestAborted = new CancellationToken(true);
            throw ioFailure ? new IOException("secret") : new OperationCanceledException("secret");
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(499, (int)response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Contains(capture.Messages, message => message.Contains("canceled by the caller; status 499"));
        Assert.Contains(LogLevel.Warning, capture.Levels);
        Assert.DoesNotContain(capture.Messages, message => message.Contains("secret"));
        Assert.Empty(capture.Exceptions);
    }

    [Fact]
    public async Task AbortedRequest_DoesNotWriteAnErrorBody()
    {
        using var capture = new AuthenticationLogCapture();
        await using var services = new ServiceCollection().AddLogging(logging => logging.AddProvider(capture)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.RequestAborted = new CancellationToken(true);
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = new OperationCanceledException() });
        await AttractionExceptionHandler.HandleAsync(context);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Contains("499", Assert.Single(capture.Messages));
        Assert.Empty(capture.Exceptions);
    }

    /**
     * Creates representative application/provider failures without any real network access.
     * @param failure The scenario under test.
     * @return The exception delivered through the production exception pipeline.
     */
    private static Exception CreateFailure(string failure) => failure switch
    {
        "serialization" or "deadlock" => new PostgresException("secret", "ERROR", "ERROR", failure == "serialization" ? "40001" : "40P01"),
        "wrapped-serialization" or "wrapped-deadlock" => new DbUpdateException("secret", CreateFailure(failure[8..])),
        "concurrency" => new DbUpdateConcurrencyException("secret"),
        "unique" or "check" => new PostgresException("secret", "ERROR", "ERROR", failure == "unique" ? "23505" : "23514"),
        "wrapped-unique" or "wrapped-check" => new DbUpdateException("secret", CreateFailure(failure[8..])),
        "unavailable" => new NpgsqlException("secret", new System.IO.IOException("secret")),
        "wrapped-unavailable" => new DbUpdateException("secret", CreateFailure("unavailable")),
        "unknown-database" => new DbUpdateException("secret"),
        "forbidden" => new UnauthorizedAccessException("secret"),
        "validation" => new ValidationException("secret"),
        "cancel" => new OperationCanceledException("secret"),
        _ => new InvalidOperationException("secret")
    };

    /**
     * Simulates distinct Users protocol failures without logging supplied sensitive data.
     * @param failure The dependency failure scenario.
     */
    private sealed class FailureHandler(string failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (failure == "timeout") throw new TaskCanceledException("secret");
            if (failure == "transport") throw new HttpRequestException("secret");
            if (failure == "unexpected-success") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            if (failure == "not-found") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (failure == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json = failure switch
            {
                "json" => "{secret",
                "status" => "{\"status\":\"secret\",\"role\":\"traveller\"}",
                "role" => "{\"status\":\"active\",\"role\":\"secret\"}",
                "null" => "null",
                _ => "{\"status\":\"active\",\"role\":\"traveller\"}"
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            if (failure == "encoding") response.Content.Headers.ContentType!.CharSet = "secret-invalid-charset";
            return Task.FromResult(response);
        }
    }
}

