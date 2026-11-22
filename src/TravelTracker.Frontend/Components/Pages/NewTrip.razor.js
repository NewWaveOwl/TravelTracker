export function watchUndo(planner) {
    const events = new AbortController();
    document.addEventListener("keydown", event => {
        if (!planner.isConnected) return;
        if (!event.defaultPrevented && !event.isComposing && !event.altKey && !event.shiftKey &&
            (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s" &&
            !(event.target instanceof Element && event.target.closest("[role=dialog], dialog"))) {
            const save = planner.querySelector("[data-trip-save]");
            if (!save) return;
            event.preventDefault();
            if (!event.repeat && !save.disabled) {
                // Blazor onchange bindings must receive the active edit before the save event.
                const active = document.activeElement;
                if (active instanceof Element && planner.contains(active) && typeof active.blur === "function") active.blur();
                save.click();
            }
            return;
        }
        if (event.defaultPrevented || event.isComposing || event.repeat || event.altKey || event.shiftKey ||
            !(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== "z") return;

        const target = event.target;
        if (target instanceof Element && (target.isContentEditable ||
            target.closest("input:not([type=checkbox]), textarea, [role=dialog]"))) return;

        const button = planner.querySelector("[data-trip-undo]:enabled");
        if (!button) return;

        event.preventDefault();
        button.click();
    }, { signal: events.signal });

    return { disconnect: () => events.abort() };
}
