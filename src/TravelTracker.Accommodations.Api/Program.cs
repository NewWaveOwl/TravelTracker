using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using TravelTracker.Accommodations.Api.Access;
using TravelTracker.Accommodations.Api.Errors;
using TravelTracker.Accommodations.Api.Services;
using TravelTracker.Accommodations.Data;
using TravelTracker.Accommodations.Data.Repositories;
using TravelTracker.Security.Authentication;
using TravelTracker.Security.Hosting;

namespace TravelTracker.Accommodations.Api;

/** Composes the independent Accommodations HTTP service and PostgreSQL store. */
public class AccommodationsProgram
{
    /**
     * Starts the Accommodations host without starting other TravelTracker services.
     * @param args Host arguments.
     * @return Completion after the host shuts down.
     */
    public static async Task Main(string[] args)
    {
        var builder = ApiHostBuilder.Create(args);
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
            options.SwaggerDoc("v1", new() { Title = "Accommodations API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.OperationFilter<BearerSecurityOperationFilter>();
        });
        builder.Services.AddDbContextFactory<AccommodationsDbContext>(options => options.UseNpgsql(
            builder.RequireDatabaseConnection("Accommodations")));
        builder.Services.AddScoped<IAccommodationRepository, EfAccommodationRepository>();
        builder.Services.AddScoped<IAccommodationService, AccommodationService>();
        builder.Services.AddScoped<CallerContext>();
        builder.Services.AddServiceAccessTokenProvider();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        var app = builder.Build();
        if (app.Environment.IsProduction())
            builder.RequireDatabaseConnection("Accommodations");
        builder.ValidateUsersDependency();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = AccommodationExceptionHandler.HandleAsync,
            SuppressDiagnosticsCallback = _ => true
        });
        var usersBaseUrl = app.Configuration["Users:InternalBaseUrl"];
        if (Uri.TryCreate(usersBaseUrl, UriKind.Absolute, out var usersUri) && usersUri.Scheme == Uri.UriSchemeHttps)
            app.Logger.LogInformation("Users access endpoint configured.");
        else
            app.Logger.LogError("Users:InternalBaseUrl must be an absolute HTTPS URL; protected requests will fail closed.");
        if (app.Configuration.ReadDatabaseFlag("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await DatabaseConfiguration.RunMigrationAsync(() => AccommodationsDatabase.InitializeAsync(
                scope.ServiceProvider.GetRequiredService<AccommodationsDbContext>()));
            app.Logger.LogInformation("Accommodations database migrations applied.");
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<AccommodationAccessMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
