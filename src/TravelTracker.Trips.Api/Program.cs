using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using TravelTracker.Api.Authentication;
using TravelTracker.Api.References;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Api.Errors;
using TravelTracker.Trips.Data;
using TravelTracker.Trips.Data.Repositories;

namespace TravelTracker.Trips.Api;

/** Composes the independent Trips HTTP service and its PostgreSQL store. */
public partial class TripsProgram
{
    /**
     * Starts the Trips host without starting the catalogue or Users services.
     *
     * @param args Host arguments.
     * @return Completion after the host shuts down.
     */
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
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
        var tripsConnectionString = builder.Configuration.GetConnectionString("Trips") ??
            throw new InvalidOperationException("ConnectionStrings:Trips is required.");
        builder.Services.AddDbContext<TripsDbContext>(options => options.UseNpgsql(tripsConnectionString));
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
        var applyMigrations = app.Configuration.GetValue<bool>("Database:ApplyMigrations");
        startupLogger.LogInformation(
            "Trips host configured: migrations on startup {ApplyMigrations}; catalogue hosts {Accommodations}, {Attractions}, {Transport}; Users {Users}; reference timeout {Timeout} s.",
            applyMigrations, references.AccommodationsBaseUrl, references.AttractionsBaseUrl, references.TransportBaseUrl,
            references.UsersBaseUrl, references.Timeout.TotalSeconds);
        if (applyMigrations)
        {
            startupLogger.LogInformation("applying Trips migrations to the configured database before accepting requests.");
            await using var scope = app.Services.CreateAsyncScope();
            await TripsDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<TripsDbContext>());
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
