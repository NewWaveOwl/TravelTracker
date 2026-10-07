using TravelTracker.Transport.Api.Data;
using TravelTracker.Transport.Api.Services;

/**
 * Starts the independent API with standard controller validation.
 * @param args Application configuration arguments.
 */
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<ITransportModeRepository, InMemoryTransportModeRepository>();
builder.Services.AddScoped<ITransportModeService, TransportModeService>();
builder.Services.AddSingleton<IRoutePointRepository, InMemoryRoutePointRepository>();
builder.Services.AddScoped<IRoutePointService, RoutePointService>();

var app = builder.Build();
app.MapControllers();
app.Run();
