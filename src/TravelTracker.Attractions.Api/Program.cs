using System.Text.Json.Serialization;
using TravelTracker.Attractions.Api.Data;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Api.Validation;

/**
 * Composes the independent Attractions API and its process-lifetime store.
 * @param args Application configuration arguments.
 */
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options => options.Filters.Add<ValidationExceptionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
    });
builder.Services.AddSingleton<InMemoryAttractionStore>();
builder.Services.AddScoped<IAttractionRepository, InMemoryAttractionRepository>();
builder.Services.AddScoped<IAttractionService, AttractionService>();

var app = builder.Build();
app.MapControllers();
app.Run();
