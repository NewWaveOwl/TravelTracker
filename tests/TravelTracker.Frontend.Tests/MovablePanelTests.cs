using Bunit;
using Microsoft.AspNetCore.Components;
using TravelTracker.Frontend.Components.Trips;

namespace TravelTracker.Frontend.Tests;

/** Proves panel accessibility, layout resets and child-state lifetime at the Blazor boundary. */
public sealed class MovablePanelTests : BunitContext
{
    [Fact]
    public void HandleAndResetAreLabelledSeparatelyFromEditableChildren()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var panel = Render<MovablePanel>(parameters => parameters
            .Add(component => component.Label, "Itinerary")
            .Add(component => component.Class, "itinerary-panel")
            .AddChildContent("<input aria-label='Trip name' value='Unsaved trip' />"));

        Assert.NotNull(panel.Find("section.itinerary-panel[aria-label='Itinerary']"));
        var handle = panel.Find("button[aria-label='Move Itinerary panel']");
        Assert.Equal("button", handle.GetAttribute("type"));
        var instructions = panel.Find($"[id='{handle.GetAttribute("aria-describedby")}']");
        Assert.Contains("arrow keys", instructions.TextContent);
        Assert.Contains("Home", handle.GetAttribute("aria-keyshortcuts"));
        Assert.NotNull(panel.Find("button[aria-label='Reset Itinerary panel position']"));
        Assert.NotNull(panel.Find(".movable-panel-body input[aria-label='Trip name']"));
    }

    [Fact]
    public void ResetButtonAndResetVersionKeepTheSameChildInstanceAndUnsavedValue()
    {
        var module = JSInterop.SetupModule("./Components/Trips/MovablePanel.razor.js");
        var controller = module.SetupModule("attach", _ => true);
        controller.SetupVoid("reset").SetVoidResult();
        var panel = Render<MovablePanel>(parameters => parameters
            .Add(component => component.Label, "Editor")
            .AddChildContent<MovablePanelTestEditor>());
        var editor = panel.FindComponent<MovablePanelTestEditor>().Instance;
        panel.Find("input").Change("Unsaved itinerary");

        panel.Find("button[aria-label='Reset Editor panel position']").Click();
        panel.Render(parameters => parameters.Add(component => component.ResetVersion, 1));
        panel.Render(parameters => parameters.Add(component => component.Label, "Trip editor"));

        Assert.Equal(2, controller.Invocations["reset"].Count);
        Assert.Single(module.Invocations["attach"]);
        Assert.Same(editor, panel.FindComponent<MovablePanelTestEditor>().Instance);
        Assert.Equal("Unsaved itinerary", panel.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task DisposalDetachesBrowserMovementHandlers()
    {
        var module = JSInterop.SetupModule("./Components/Trips/MovablePanel.razor.js");
        var controller = module.SetupModule("attach", _ => true);
        controller.SetupVoid("dispose").SetVoidResult();
        var panel = Render<MovablePanel>(parameters => parameters.Add(component => component.Label, "Tools"));

        await panel.Instance.DisposeAsync();

        Assert.Single(controller.Invocations["dispose"]);
    }

    /** A stateful child used to detect panel resets that accidentally remount editors. */
    public sealed class MovablePanelTestEditor : ComponentBase
    {
        private string name = "";

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "value", name);
            builder.AddAttribute(2, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, args => name = args.Value?.ToString() ?? ""));
            builder.CloseElement();
        }
    }
}
