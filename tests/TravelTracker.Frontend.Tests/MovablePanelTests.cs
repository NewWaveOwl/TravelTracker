using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Components.Trips;

namespace TravelTracker.Frontend.Tests;

/** Proves panel accessibility, layout resets and child-state lifetime at the Blazor boundary. */
public sealed class MovablePanelTests : BunitContext
{
    [Fact]
    public void IconHandleIsLabelledSeparatelyFromEditableChildren()
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
        Assert.Empty(handle.TextContent.Trim());
        Assert.Single(handle.QuerySelectorAll("img[alt='']"));
        Assert.Single(panel.FindAll("button"));
        Assert.NotNull(panel.Find(".movable-panel-body input[aria-label='Trip name']"));
    }

    [Fact]
    public void ChangingViewsDetachesAndReattachesMovementWithoutRemountingEditors()
    {
        var module = JSInterop.SetupModule("./Components/Trips/MovablePanel.razor.js");
        var controller = module.SetupModule("attach", _ => true);
        controller.SetupVoid("reset").SetVoidResult();
        controller.SetupVoid("dispose").SetVoidResult();
        var panel = Render<MovablePanel>(parameters => parameters
            .Add(component => component.Label, "Editor")
            .Add(component => component.Enabled, false)
            .AddChildContent<MovablePanelTestEditor>());
        var editor = panel.FindComponent<MovablePanelTestEditor>().Instance;
        panel.Find("input").Change("Unsaved itinerary");
        Assert.Empty(panel.FindAll(".movable-panel-handle"));
        Assert.Empty(module.Invocations["attach"]);

        panel.Render(parameters => parameters.Add(component => component.Enabled, true));
        Assert.Single(panel.FindAll(".movable-panel-handle"));
        panel.Render(parameters => parameters.Add(component => component.Enabled, false));
        Assert.Single(controller.Invocations["reset"]);
        Assert.Single(controller.Invocations["dispose"]);
        panel.Render(parameters => parameters.Add(component => component.Enabled, true));

        Assert.Equal(2, module.Invocations["attach"].Count);
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

    [Fact]
    public async Task DisposingDuringImportDisposesTheLateBrowserReference()
    {
        var runtime = new PendingPanelRuntime();
        Services.AddSingleton<IJSRuntime>(runtime);
        var panel = Render<MovablePanel>(parameters => parameters.Add(component => component.Label, "Tools"));
        var module = new LatePanelModule();
        await panel.Instance.DisposeAsync();
        runtime.Import.SetResult(module);
        panel.WaitForAssertion(() => Assert.Equal(1, module.DisposeCount));
    }

    /** Defers the real import boundary so disposal can occur while the browser is still answering. */
    sealed class PendingPanelRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "import") throw new InvalidOperationException("Only the pending browser import is expected.");
            return (TValue)(object)(await Import.Task);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    /** Records disposal of a browser reference returned after its component has closed. */
    sealed class LatePanelModule : IJSObjectReference
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("A closed panel must not attach browser movement handlers.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
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
