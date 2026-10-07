using TravelTracker.Attractions.Api.Data;
namespace TravelTracker.Attractions.Api.Services;

/**
 * Coordinates catalogue operations, implemented in separate feature files.
 * @param repository Scoped persistence abstraction.
 */
public sealed partial class AttractionService(IAttractionRepository repository) : IAttractionService;
