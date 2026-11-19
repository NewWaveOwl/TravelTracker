using TravelTracker.Security.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TravelTracker.Security.Authentication;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories;

namespace TravelTracker.Users.Api;

/** Composes the independently deployed Users bootstrap and account-access service. */
public class UsersProgram
{
    /**
     * Starts the Users host with deployment-owned PostgreSQL and identity settings.
     * @param args Host arguments.
     * @return Completion when the host shuts down.
     */
    public static async Task Main(string[] args)
    {
        var builder = ApiHostBuilder.Create(args);
        builder.Services.AddControllers();
        builder.Services.AddApiBearerAuthentication(builder.Configuration);
        // lazy so late configuration overrides are honored; read once, not per request
        var allowedServiceSubjects = new Lazy<HashSet<string?>>(() => builder.Configuration.GetSection("UsersAccess:AllowedServiceSubjects")
            .GetChildren().Select(entry => entry.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal));
        builder.Services.AddOptions<AuthorizationOptions>().Configure<IOptions<BearerSettings>>((options, settings) =>
        {
            foreach (var scope in new[] { "users.read", "users.write", "users.access", "users.admin" })
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, scope) &&
                    (scope != ScopePolicies.UsersAccess ||
                        context.User.FindFirstValue("sub") is { } subject && allowedServiceSubjects.Value.Contains(subject))));
        });
        builder.Services.AddDbContextFactory<UsersDbContext>(options => options.UseNpgsql(
            // read lazily on purpose: health must answer without database configuration
            builder.Configuration.GetConnectionString("Users") ?? throw new InvalidOperationException("ConnectionStrings:Users is required.")));
        builder.Services.AddScoped<IUserRepository, EfUserRepository>();
        builder.Services.AddScoped<TravelTracker.Users.Data.Repositories.Profile.IUserRepository,
            TravelTracker.Users.Data.Repositories.Profile.UserRepository>();
        builder.Services.AddScoped<UserProfileService>();
        builder.Services.AddScoped<BootstrapService>();
        builder.Services.AddHttpClient<VerifiedIdentityClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var app = builder.Build();
        app.Logger.LogInformation("Users host starting; ApplyMigrations={ApplyMigrations}, AllowedServiceSubjects={ServiceSubjectCount}, Bootstrap configured={BootstrapConfigured}, ServiceToken configured={ServiceTokenConfigured}.",
            app.Configuration.GetValue<bool>("Database:ApplyMigrations"), allowedServiceSubjects.Value.Count,
            !string.IsNullOrWhiteSpace(app.Configuration["Bootstrap:Email"]) && !string.IsNullOrWhiteSpace(app.Configuration["Bootstrap:Issuer"]) && app.Configuration["Bootstrap:Secret"] is { Length: >= 32 },
            app.Configuration["UsersAccess:ServiceToken"] is { Length: >= 32 });
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Database.MigrateAsync();
            app.Logger.LogInformation("Users database migrations applied.");
        }
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                app.Logger.LogDebug("Users request {TraceIdentifier} was aborted by the client.", context.TraceIdentifier);
            }
            catch (Exception exception)
            {
                // the route template is logged instead of the path, because the path carries account identifiers
                var route = context.GetEndpoint() is RouteEndpoint { } endpoint ? endpoint.RoutePattern.RawText : "unmatched";
                // every failure here answers 503, so only the exception type is logged; provider messages can carry connection details
                app.Logger.LogError("Users request failed ({FailureType}) on {Method} {Route} (trace {TraceIdentifier}).",
                    exception.GetType().Name,
                    context.Request.Method, route, context.TraceIdentifier);
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Account service is temporarily unavailable.").ExecuteAsync(context);
                }
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.RunAsync();
    }
}
