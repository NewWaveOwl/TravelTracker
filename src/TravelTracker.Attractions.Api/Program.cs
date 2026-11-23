using TravelTracker.Security.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using TravelTracker.Security.Authentication;
using TravelTracker.Attractions.Api.Access;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Api.Errors;
using TravelTracker.Attractions.Data;
using TravelTracker.Attractions.Data.Repositories;

namespace TravelTracker.Attractions.Api;

/**
 * Independently composes Attractions, its PostgreSQL store and its HTTP access boundaries.
 */
public partial class AttractionsProgram
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
            options.SwaggerDoc("v1", new() { Title = "Attractions API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
            options.OperationFilter<BearerSecurityOperationFilter>();
        });
        builder.Services.AddDbContextFactory<AttractionsDbContext>(options => options.UseNpgsql(
            builder.RequireDatabaseConnection("Attractions")));
        builder.Services.AddSingleton(provider => new EntityIdGenerator(cancellationToken =>
            AttractionsDatabase.HighestKeyAsync(provider.GetRequiredService<IDbContextFactory<AttractionsDbContext>>(), cancellationToken)));
        builder.Services.AddScoped<IAttractionRepository, EfAttractionRepository>();
        builder.Services.AddScoped<IAttractionService, AttractionService>();
        builder.Services.AddScoped<CallerContext>();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var app = builder.Build();
        if (app.Environment.IsProduction())
            builder.RequireDatabaseConnection("Attractions");
        builder.ValidateUsersDependency();
        app.UseAttractionExceptionHandling();
        var internalBase = app.Configuration["Users:InternalBaseUrl"];
        var usersEndpoint = string.IsNullOrWhiteSpace(internalBase) ? app.Configuration["Users:AccessEndpoint"] : internalBase;
        if (Uri.TryCreate(usersEndpoint, UriKind.Absolute, out var usersUri) && usersUri.Scheme == Uri.UriSchemeHttps)
            app.Logger.LogInformation("Users access endpoint configured.");
        else
            app.Logger.LogError("Users access endpoint must be an absolute HTTPS URL; protected requests will fail closed.");
        if (app.Configuration.ReadDatabaseFlag("Database:ApplyMigrations"))
        {
            var seedExamples = app.Configuration.ReadDatabaseFlag("Database:SeedExamples");
            await using var scope = app.Services.CreateAsyncScope();
            var seededCount = await DatabaseConfiguration.RunMigrationAsync(() => AttractionsDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<AttractionsDbContext>(), seedExamples));
            var seedState = !seedExamples ? "disabled" : seededCount > 0 ? $"inserted {seededCount} record(s)" : "skipped, the table already has rows";
            app.Logger.LogInformation("Attractions database migrations applied; example seeding {SeedState}.", seedState);
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<AttractionAccessMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
