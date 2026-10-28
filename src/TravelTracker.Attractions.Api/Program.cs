using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using Microsoft.OpenApi;
using TravelTracker.Api.Authentication;
using TravelTracker.Attractions.Api.Access;
using TravelTracker.Attractions.Api.Services;
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
        var builder = WebApplication.CreateBuilder(args);
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
            builder.Configuration.GetConnectionString("Attractions") ?? throw new InvalidOperationException("ConnectionStrings:Attractions is required.")));
        builder.Services.AddSingleton(provider => new EntityIdGenerator(cancellationToken =>
            AttractionsDatabase.HighestKeyAsync(provider.GetRequiredService<IDbContextFactory<AttractionsDbContext>>(), cancellationToken)));
        builder.Services.AddScoped<IAttractionRepository, EfAttractionRepository>();
        builder.Services.AddScoped<IAttractionService, AttractionService>();
        builder.Services.AddScoped<CallerContext>();
        builder.Services.AddHttpClient<UsersAccessClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        var app = builder.Build();
        app.UseExceptionHandler(error => error.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var status = exception is NpgsqlException or DbUpdateException ? 503 : 500;
            await Results.Problem(statusCode: status, title: status == 503
                ? "Attraction storage is unavailable." : "The request could not be completed.").ExecuteAsync(context);
        }));
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await AttractionsDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<AttractionsDbContext>(),
                app.Configuration.GetValue<bool>("Database:SeedExamples"));
        }
        if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<AttractionAccessMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
