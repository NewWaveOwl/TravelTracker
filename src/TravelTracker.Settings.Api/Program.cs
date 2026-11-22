using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using TravelTracker.Security.Authentication;
using TravelTracker.Settings.Api.Access;
using TravelTracker.Settings.Api.Errors;
using TravelTracker.Settings.Api.Services;
using TravelTracker.Settings.Data;
using TravelTracker.Settings.Data.Repositories;
using TravelTracker.Security.Hosting;

namespace TravelTracker.Settings.Api;

/** composes the independent Settings HTTP service and its PostgreSQL store. */
public class SettingsProgram
{
    /**
     * starts the Settings host without starting other TravelTracker services.
     * @param args host arguments.
     * @return completion after the host shuts down.
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
            options.SwaggerDoc("v1", new() { Title = "Settings API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.OperationFilter<BearerSecurityOperationFilter>();
        });
        builder.Services.AddDbContextFactory<SettingsDbContext>(options => options.UseNpgsql(
            builder.RequireDatabaseConnection("Settings")));
        builder.Services.AddScoped<ISettingsRepository, EfSettingsRepository>();
        builder.Services.AddScoped<ISettingsService, SettingsService>();
        builder.Services.AddScoped<CallerContext>();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        var app = builder.Build();
        if (app.Environment.IsProduction())
            builder.RequireDatabaseConnection("Settings");
        builder.ValidateUsersDependency();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = SettingsExceptionHandler.HandleAsync,
            SuppressDiagnosticsCallback = _ => true
        });
        if (Uri.TryCreate(app.Configuration["Users:InternalBaseUrl"], UriKind.Absolute, out var usersUri) && usersUri.Scheme == Uri.UriSchemeHttps)
            app.Logger.LogInformation("Users access endpoint configured.");
        else
            app.Logger.LogError("Users:InternalBaseUrl must be an absolute HTTPS URL; protected requests will fail closed.");
        if (app.Configuration.ReadDatabaseFlag("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await DatabaseConfiguration.RunMigrationAsync(() => SettingsDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<SettingsDbContext>()));
            app.Logger.LogInformation("Settings database migrations applied.");
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<SettingsAccessMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
