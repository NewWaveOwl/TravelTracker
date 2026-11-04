using Microsoft.EntityFrameworkCore;
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
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        builder.Services.AddApiBearerAuthentication(builder.Configuration);
        builder.Services.AddDbContextFactory<UsersDbContext>(options => options.UseNpgsql(
            builder.Configuration.GetConnectionString("Users") ?? throw new InvalidOperationException("ConnectionStrings:Users is required.")));
        builder.Services.AddScoped<IUserRepository, EfUserRepository>();
        builder.Services.AddScoped<BootstrapService>();
        builder.Services.AddHttpClient<VerifiedIdentityClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
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
