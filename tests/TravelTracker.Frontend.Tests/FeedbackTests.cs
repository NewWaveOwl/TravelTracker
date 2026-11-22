using Bunit;
using TravelTracker.Frontend.Components.Shared;

namespace TravelTracker.Frontend.Tests;

/** Verifies feedback announcements and recovery actions independently of their visual palette. */
public sealed class FeedbackTests : BunitContext
{
    [Theory]
    [InlineData(FeedbackKind.Loading, "status", "LOADING")]
    [InlineData(FeedbackKind.Empty, "status", "EMPTY")]
    [InlineData(FeedbackKind.Validation, "alert", "CHECK YOUR INPUT")]
    [InlineData(FeedbackKind.Permission, "alert", "ACCESS REQUIRED")]
    [InlineData(FeedbackKind.Network, "alert", "CONNECTION ERROR")]
    [InlineData(FeedbackKind.Unavailable, "alert", "SERVICE UNAVAILABLE")]
    public void StatesKeepTheirAnnouncementAndDefaultLabel(FeedbackKind kind, string role, string label)
    {
        var panel = Render<Feedback>(parameters => parameters.Add(component => component.Kind, kind).AddChildContent("Your entered details are retained."));

        Assert.Equal(role, panel.Find("section").GetAttribute("role"));
        Assert.Equal(label, panel.Find("small").TextContent);
        Assert.Equal(kind == FeedbackKind.Loading ? "true" : "false", panel.Find("section").GetAttribute("aria-busy"));
        Assert.Contains("Your entered details are retained.", panel.Find("p").TextContent);
    }

    [Fact]
    public void NetworkRecoveryRetriesAndKeepsExplicitPageCopy()
    {
        var retries = 0;
        var panel = Render<Feedback>(parameters => parameters
            .Add(component => component.Kind, FeedbackKind.Network)
            .Add(component => component.Title, "Your trip is still here")
            .Add(component => component.Eyebrow, "TRIP SAVE")
            .Add(component => component.Retry, () => retries++));

        panel.Find("button").Click();

        Assert.Equal(1, retries);
        Assert.Equal("Your trip is still here", panel.Find("h2").TextContent);
        Assert.Equal("TRIP SAVE", panel.Find("small").TextContent);
        Assert.Equal("/", panel.Find("a").GetAttribute("href"));
    }
}
