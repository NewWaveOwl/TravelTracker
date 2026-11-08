using TravelTracker.Security.Hosting;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Api.Errors;
using Microsoft.OpenApi;
using TravelTracker.Api.Authentication;
using TravelTracker.Transport.Api.Services;
using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;

namespace TravelTracker.Transport.Api;

/**

 * Independently composes Transport, its PostgreSQL store and its HTTP access boundaries.

 */
public partial class TransportProgram
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
            builder.Configuration.GetConnectionString("Transport") ?? throw new InvalidOperationException("ConnectionStrings:Transport is required.")));
        builder.Services.AddSingleton(provider => new EntityIdGenerator(cancellationToken =>
            TransportDatabase.HighestKeyAsync(provider.GetRequiredService<IDbContextFactory<TransportDbContext>>(), cancellationToken)));
        builder.Services.AddScoped<ITransportModeRepository, EfTransportModeRepository>();
        builder.Services.AddScoped<IRoutePointRepository, EfRoutePointRepository>();
        builder.Services.AddScoped<ITransportModeService, TransportModeService>();
        builder.Services.AddScoped<IRoutePointService, RoutePointService>();
        var app = builder.Build();
        app.UseTransportExceptionHandling();
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await TransportDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<TransportDbContext>(),
                app.Configuration.GetValue<bool>("Database:SeedExamples"));
        }
        if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.RunAsync();
    }
}
