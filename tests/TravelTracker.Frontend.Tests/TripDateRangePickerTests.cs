using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Components.Trips;

namespace TravelTracker.Frontend.Tests;

/** Exercises one shared calendar through its date, month, keyboard and cancellation controls. */
public sealed class TripDateRangePickerTests : BunitContext
{
    public TripDateRangePickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse("2026-11-14T12:00:00Z")));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(17)]
    public void BeginningIsProvisionalAndEndingCommitsOneInclusiveRange(int ending)
    {
        (DateOnly Start, DateOnly End)? selected = null;
        var changes = 0;
        var calendar = Render<TripDateRangePicker>(parameters => parameters.Add(component => component.RangeChanged,
            range => { selected = range; changes++; }));
        calendar.Find(".range-trigger").Click();
        calendar.Find("[data-date='2026-11-14']").Click();
        Assert.Null(selected);
        Assert.Equal("true", calendar.Find("[data-date='2026-11-14']").GetAttribute("aria-selected"));
        calendar.Find($"[data-date='2026-11-{ending}']").Click();
        Assert.Equal((new DateOnly(2026, 11, 14), new DateOnly(2026, 11, ending)), selected);
        Assert.Equal(1, changes);
        Assert.Empty(calendar.FindAll("[role=dialog]"));
    }

    [Fact]
    public void EarlierEndingRestartsTheBeginningAndMonthNavigationKeepsIt()
    {
        (DateOnly Start, DateOnly End)? selected = null;
        var calendar = Render<TripDateRangePicker>(parameters => parameters.Add(component => component.RangeChanged, range => selected = range));
        calendar.Find(".range-trigger").Click();
        calendar.Find("[data-date='2026-11-30']").Click();
        calendar.Find("[data-date='2026-11-29']").Click();
        Assert.Null(selected);
        calendar.Find("[aria-label='Next month']").Click();
        calendar.Find("[data-date='2026-12-02']").Click();
        Assert.Equal((new DateOnly(2026, 11, 29), new DateOnly(2026, 12, 2)), selected);
    }

    [Fact]
    public void KeyboardMovesAndSelectsDatesAcrossWeeks()
    {
        (DateOnly Start, DateOnly End)? selected = null;
        var calendar = Render<TripDateRangePicker>(parameters => parameters.Add(component => component.RangeChanged, range => selected = range));
        calendar.Find(".range-trigger").Click();
        calendar.Find("[role=grid]").KeyDown("ArrowRight");
        calendar.Find("[role=grid]").KeyDown("Enter");
        calendar.Find("[role=grid]").KeyDown("ArrowDown");
        calendar.Find("[role=grid]").KeyDown(" ");
        Assert.Equal((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 22)), selected);
    }

    [Fact]
    public void TabAndUnrecognizedKeysDoNotRefocusOrTrapTheCalendar()
    {
        var calendar = Render<TripDateRangePicker>();
        calendar.Find(".range-trigger").Click();
        var focusCalls = JSInterop.Invocations.Count(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal));
        calendar.Find("[role=grid]").KeyDown("Tab");
        calendar.Find("[role=grid]").KeyDown("a");
        Assert.Equal(focusCalls, JSInterop.Invocations.Count(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal)));
        Assert.Single(calendar.FindAll("[role=dialog]"));
    }

    [Fact]
    public void CancelKeepsExistingPastDatesAndDiscardsTheProvisionalBeginning()
    {
        var changes = 0;
        var calendar = Render<TripDateRangePicker>(parameters => parameters
            .Add(component => component.Start, new DateOnly(2025, 11, 10))
            .Add(component => component.End, new DateOnly(2025, 11, 12))
            .Add(component => component.RangeChanged, _ => changes++));
        calendar.Find(".range-trigger").Click();
        Assert.Equal(3, calendar.FindAll("[aria-selected=true]").Count);
        calendar.Find("[data-date='2025-11-15']").Click();
        calendar.Find("[aria-label='Close calendar']").Click();
        calendar.Find(".range-trigger").Click();
        Assert.Equal(0, changes);
        Assert.Equal("true", calendar.Find("[data-date='2025-11-10']").GetAttribute("aria-selected"));
        calendar.Find("[role=grid]").KeyDown("Escape");
        Assert.Empty(calendar.FindAll("[role=dialog]"));
    }
}
