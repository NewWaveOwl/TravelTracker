using TravelTracker.Data;
using TravelTracker.Data.Repositories;
using TravelTracker.Api.Services;
using TravelTracker.Api.Authentication;
using Microsoft.OpenApi;

/**
 * Starts one API host for Attractions, Transport entries and Route Points.
 * @param args Application configuration arguments.
 */
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddApiBearerAuthentication(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TravelTracker API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter an access token issued for this API. Read and write permissions are separate."
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});
builder.Services.AddSingleton(EntityIdGenerator.ForSeedData());
builder.Services.AddSingleton<IAttractionRepository, InMemoryAttractionRepository>();
builder.Services.AddScoped<IAttractionService, AttractionService>();
builder.Services.AddSingleton<ITransportModeRepository, InMemoryTransportModeRepository>();
builder.Services.AddScoped<ITransportModeService, TransportModeService>();
builder.Services.AddSingleton<IRoutePointRepository, InMemoryRoutePointRepository>();
builder.Services.AddScoped<IRoutePointService, RoutePointService>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
