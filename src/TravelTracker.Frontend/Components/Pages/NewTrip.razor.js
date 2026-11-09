export function watchUndo(planner) {
    const events = new AbortController();
    document.addEventListener("keydown", event => {
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
