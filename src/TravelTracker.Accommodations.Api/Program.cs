using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using TravelTracker.Api.Authentication;
using TravelTracker.Accommodations.Api.Access;
using TravelTracker.Accommodations.Api.Errors;
using TravelTracker.Accommodations.Api.Services;
using TravelTracker.Accommodations.Data;
using TravelTracker.Accommodations.Data.Repositories;

namespace TravelTracker.Accommodations.Api;

/** Composes the independent Accommodations HTTP service and PostgreSQL store. */
public partial class AccommodationsProgram
{
    /**
     * Starts the Accommodations host without starting other TravelTracker services.
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
            builder.Configuration.GetConnectionString("Accommodations") ??
            throw new InvalidOperationException("ConnectionStrings:Accommodations is required.")));
        builder.Services.AddScoped<IAccommodationRepository, EfAccommodationRepository>();
        builder.Services.AddScoped<IAccommodationService, AccommodationService>();
        builder.Services.AddScoped<CallerContext>();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        var app = builder.Build();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = AccommodationExceptionHandler.HandleAsync,
            SuppressDiagnosticsCallback = _ => true
        });
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await AccommodationsDatabase.InitializeAsync(
                scope.ServiceProvider.GetRequiredService<AccommodationsDbContext>());
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
