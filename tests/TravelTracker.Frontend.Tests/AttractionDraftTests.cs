using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Features.Attractions;

namespace TravelTracker.Frontend.Tests;

public sealed class AttractionDraftTests
{
    static AttractionDto Example => new(Guid.Parse("00000000-0000-0000-0000-000000000123"), "Museum", "Old description", 4m, "Montreal", 45.48m, -73.57m, 120);

    [Fact]
    public void PatchOmitsUnchangedFieldsAndExplicitlyClearsOptionalValues()
    {
        var draft = AttractionDraft.From(Example);
        draft.Description = "";
        draft.Rating = "";
        draft.ApproximationMinutes = "30";

        var patch = draft.PatchBody(Example);

        Assert.Equal(2, patch.Count);
        Assert.Null(patch["description"]);
        Assert.Null(patch["rating"]);
        Assert.DoesNotContain("approximationMinutes", patch.Keys);
        Assert.Equal("90–150 minutes", draft.RangePreview());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void RejectsInvalidDurationAndUncertainty(string value)
    {
        var draft = AttractionDraft.From(Example);
        draft.EstimatedVisitMinutes = value;
        draft.ApproximationMinutes = value;

        Assert.Contains("estimatedVisitMinutes", draft.Validate().Keys);
        Assert.Contains("approximationMinutes", draft.Validate().Keys);
        Assert.Null(draft.RangePreview());
    }

    [Fact]
    public void RangeDoesNotUnderflowOrOverflowWholeMinuteInputs()
    {
        var draft = AttractionDraft.From(Example);
        draft.EstimatedVisitMinutes = "2147483647";
        draft.ApproximationMinutes = "2147483647";
        Assert.Equal("0–4294967294 minutes", draft.RangePreview());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ClearingAnEmptyOrWhitespaceSavedDescriptionSendsExplicitNull(string description)
    {
        var original = Example with { Description = description };
        var draft = AttractionDraft.From(original);
        draft.Description = "";

        var patch = draft.PatchBody(original);

        Assert.Single(patch);
        Assert.Null(patch["description"]);
        Assert.DoesNotContain("attractionId", patch.Keys);
        Assert.Equal(original.AttractionId, Example.AttractionId);
    }

    [Fact]
    public void EditingAnotherFieldPreservesAnUntouchedWhitespaceDescription()
    {
        var original = Example with { Description = "   " };
        var draft = AttractionDraft.From(original);
        draft.Name = "New museum name";

        var patch = draft.PatchBody(original);

        Assert.Single(patch);
        Assert.Equal("New museum name", patch["name"]);
        Assert.DoesNotContain("description", patch.Keys);
    }

    [Theory]
    [InlineData("NaN", "0", "4")]
    [InlineData("91", "0", "4")]
    [InlineData("45", "181", "4")]
    [InlineData("45", "-73", "5.1")]
    public void RejectsOutOfContractCoordinatesAndRating(string lat, string lon, string rating)
    {
        var draft = AttractionDraft.From(Example);
        draft.Lat = lat; draft.Lon = lon; draft.Rating = rating;
        Assert.NotEmpty(draft.Validate());
    }
}
