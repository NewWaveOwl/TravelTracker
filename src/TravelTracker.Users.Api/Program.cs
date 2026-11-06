using TravelTracker.Security.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TravelTracker.Api.Authentication;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories;

namespace TravelTracker.Users.Api;

/** Composes the independently deployed Users bootstrap and account-access service. */
public partial class UsersProgram
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
        builder.Services.AddOptions<AuthorizationOptions>().Configure<IOptions<BearerSettings>>((options, settings) =>
        {
            foreach (var scope in new[] { "users.read", "users.write", "users.access" })
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, scope) &&
                    (scope != "users.access" || builder.Configuration.GetSection("UsersAccess:AllowedServiceSubjects")
                        .GetChildren().Any(entry => !string.IsNullOrWhiteSpace(entry.Value) &&
                            string.Equals(entry.Value, context.User.FindFirstValue("sub"), StringComparison.Ordinal)))));
        });
        builder.Services.AddDbContextFactory<UsersDbContext>(options => options.UseNpgsql(
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
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Database.MigrateAsync();
        }
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                app.Logger.LogError("Users request failed ({FailureType}).", exception.GetType().Name);
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    await Results.Problem(statusCode: 503, title: "Account service is temporarily unavailable.").ExecuteAsync(context);
                }
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.RunAsync();
    }
}
