using TravelTracker.Security.Hosting;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Api.Errors;
using Microsoft.OpenApi;
using TravelTracker.Security.Authentication;
using TravelTracker.Transport.Api.Access;
using TravelTracker.Transport.Api.Services;
using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;

namespace TravelTracker.Transport.Api;

/** Independently composes Transport, its PostgreSQL store and its HTTP access boundaries. */
public class TransportProgram
{
    /**
     * Starts the independent host without starting other TravelTracker hosts.
     * @param args Environment and host arguments.
     * @return Completion when the host shuts down.
     */
    public static async Task Main(string[] args)
    {
        var builder = ApiHostBuilder.Create(args);
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddApiBearerAuthentication(builder.Configuration);
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Transport API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
            options.OperationFilter<BearerSecurityOperationFilter>();
        });
        builder.Services.AddDbContextFactory<TransportDbContext>(options => options.UseNpgsql(
            builder.RequireDatabaseConnection("Transport")));
        builder.Services.AddSingleton(provider => new EntityIdGenerator(cancellationToken =>
            TransportDatabase.HighestKeyAsync(provider.GetRequiredService<IDbContextFactory<TransportDbContext>>(), cancellationToken)));
        builder.Services.AddScoped<ITransportModeRepository, EfTransportModeRepository>();
        builder.Services.AddScoped<IRoutePointRepository, EfRoutePointRepository>();
        builder.Services.AddScoped<ITransportModeService, TransportModeService>();
        builder.Services.AddScoped<IRoutePointService, RoutePointService>();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var app = builder.Build();
        if (app.Environment.IsProduction())
            builder.RequireDatabaseConnection("Transport");
        builder.ValidateUsersDependency();
        app.UseTransportExceptionHandling();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<TransportProgram>();
        var applyMigrations = app.Configuration.ReadDatabaseFlag("Database:ApplyMigrations");
        var seedExamples = app.Configuration.ReadDatabaseFlag("Database:SeedExamples");
        logger.LogInformation("Transport starting in {Environment} (apply migrations: {ApplyMigrations}, seed examples: {SeedExamples}).", app.Environment.EnvironmentName, applyMigrations, seedExamples);
        if (applyMigrations)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var seededCount = await DatabaseConfiguration.RunMigrationAsync(() => TransportDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<TransportDbContext>(), seedExamples));
            var seedState = !seedExamples ? "disabled" : seededCount > 0 ? $"inserted {seededCount} record(s)" : "skipped, the tables already have rows";
            logger.LogInformation("Transport migrations applied (seeding {SeedState}).", seedState);
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<TransportAccessMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
