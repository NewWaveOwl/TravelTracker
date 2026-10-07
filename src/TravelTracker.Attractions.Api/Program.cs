using TravelTracker.Attractions.Api.Data;
using TravelTracker.Attractions.Api.Services;

/**
 * Starts the independent API with standard controller validation.
 * @param args Application configuration arguments.
 */
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<IAttractionRepository, InMemoryAttractionRepository>();
builder.Services.AddScoped<IAttractionService, AttractionService>();

var app = builder.Build();
app.MapControllers();
app.Run();
