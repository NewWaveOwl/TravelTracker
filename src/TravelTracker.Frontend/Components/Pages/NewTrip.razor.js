export function watchUndo(planner) {
    const events = new AbortController();
    document.addEventListener("keydown", event => {
        if (!planner.isConnected) return;
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

/**
 * Routes the editor save shortcut through the same visible button as pointer activation.
 * @param planner The mounted editor root; navigation removes its shortcut scope.
 * @return The registration to disconnect when the editor closes.
 */
export function watchSave(planner) {
    const events = new AbortController();
    document.addEventListener("keydown", event => {
        if (event.defaultPrevented || event.isComposing || event.altKey || event.shiftKey ||
            !(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== "s" || !planner.isConnected) return;

        const target = event.target;
        if (target instanceof Element && (target.closest("[role=dialog], [role=alertdialog], dialog") ||
            !planner.contains(target) && target !== document.body && target !== document.documentElement)) return;

        const button = planner.querySelector("[data-trip-save]");
        if (!button) return;

        // Suppress native Save Page even during an in-flight save, without submitting twice.
        event.preventDefault();
        if (event.repeat || button.disabled) return;

        // Programmatic clicks do not blur: commit onchange-bound values before Blazor handles save.
        if (target instanceof Element && target.matches("input, textarea, select") && planner.contains(target))
            target.dispatchEvent(new Event("change", { bubbles: true }));
        button.click();
    }, { signal: events.signal });

    return { disconnect: () => events.abort() };
}

/**
 * Registers the editor's save and existing undo shortcuts for one component lifetime.
 * @param planner The mounted editor root.
 * @return The combined registration released on navigation.
 */
export function watchEditorShortcuts(planner) {
    const undo = watchUndo(planner);
    const save = watchSave(planner);
    const events = new AbortController();
    document.addEventListener("keydown", event => {
        if (event.key !== "Delete" || event.defaultPrevented || event.repeat || event.isComposing ||
            event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || !planner.isConnected) return;
        const target = event.target;
        if (!(target instanceof Element) || target.isContentEditable ||
            target.matches("input, textarea, select") || target.closest("dialog, [role=dialog], [role=alertdialog]") ||
            !planner.contains(target) && target !== document.body && target !== document.documentElement) return;
        const button = planner.querySelector("[data-trip-delete]:enabled");
        if (!button) return;
        event.preventDefault();
        button.click();
    }, { signal: events.signal });
    return { disconnect: () => { undo.disconnect(); save.disconnect(); events.abort(); } };
}
