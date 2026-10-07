using System.Text.Json.Serialization;
using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Data;
using TravelTracker.Transport.Api.Services;
using TravelTracker.Transport.Api.Validation;

/**
 * Composes the independent Transport API and its process-lifetime store.
 * @param args Application configuration arguments.
 */
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options => options.Filters.Add<ValidationExceptionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
        options.JsonSerializerOptions.Converters.Add(new OffsetTimestampJsonConverter());
    });
builder.Services.AddSingleton<InMemoryTransportStore>();
builder.Services.AddScoped<ITransportModeRepository, InMemoryTransportModeRepository>();
builder.Services.AddScoped<IRoutePointRepository, InMemoryRoutePointRepository>();
builder.Services.AddScoped<ITransportRouteRepository, InMemoryTransportRouteRepository>();
builder.Services.AddScoped<ITransportModeService, TransportModeService>();
builder.Services.AddScoped<IRoutePointService, RoutePointService>();
builder.Services.AddScoped<ITransportRouteService, TransportRouteService>();

var app = builder.Build();
app.MapControllers();
app.Run();
