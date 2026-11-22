using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using TravelTracker.Security.Authentication;
using TravelTracker.Api.References;
using TravelTracker.Api.Trips;
using TravelTracker.Security.Hosting;
using TravelTracker.Trips.Api.Errors;
using TravelTracker.Trips.Data;
using TravelTracker.Trips.Data.Repositories;

namespace TravelTracker.Trips.Api;

/** composes the independent Trips HTTP service and its PostgreSQL store. */
public class TripsProgram
{
    /**
     * starts the Trips host without starting the catalogue or Users services.
     *
     * @param args host arguments.
     * @return completion after the host shuts down.
     */
    public static async Task Main(string[] args)
    {
        var builder = ApiHostBuilder.Create(args);
        var connectionString = builder.RequireDatabaseConnection("Trips");
        if (builder.Environment.IsProduction())
        {
            foreach (var key in new[] { "TripReferences:AccommodationsBaseUrl", "TripReferences:AttractionsBaseUrl", "TripReferences:TransportBaseUrl" })
                DependencyConfiguration.RequireHttps(builder.Configuration[key], key);
        }
        builder.Services.AddControllers();
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(
                        entry => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                        entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());
                return new BadRequestObjectResult(new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest
                });
            };
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddApiBearerAuthentication(builder.Configuration);
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Trips API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.OperationFilter<BearerSecurityOperationFilter>();
        });
        builder.Services.AddDbContext<TripsDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<ITripRepository, EfTripRepository>();
        builder.Services.AddScoped<TripReadService>();
        builder.Services.AddTripReferences(builder.Configuration);

        var app = builder.Build();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = TripExceptionHandler.HandleAsync,
            SuppressDiagnosticsCallback = _ => true
        });
        var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TripsStartup");
        var references = app.Services.GetRequiredService<IOptions<TripReferenceOptions>>().Value;
        var applyMigrations = app.Configuration.ReadDatabaseFlag("Database:ApplyMigrations");
        startupLogger.LogInformation(
            "Trips host configured: migrations on startup {ApplyMigrations}; reference timeout {Timeout} s.",
            applyMigrations, references.Timeout.TotalSeconds);
        if (applyMigrations)
        {
            startupLogger.LogInformation("applying Trips migrations to the configured database before accepting requests.");
            await using var scope = app.Services.CreateAsyncScope();
            await DatabaseConfiguration.RunMigrationAsync(() => TripsDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<TripsDbContext>()));
            startupLogger.LogInformation("Trips migrations are applied.");
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.RunAsync();
    }
}
