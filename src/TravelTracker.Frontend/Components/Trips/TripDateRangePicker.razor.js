/**
 * Prevents calendar navigation keys from also scrolling the page; Tab retains native focus movement.
 * @param {HTMLElement} grid The calendar keyboard surface.
 * @return {{dispose: Function}} Removes the keyboard guard when the calendar closes.
 */
export function attach(grid) {
    const keys = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown", "Enter", " "]);
    const guard = event => {
        if (!event.altKey && !event.ctrlKey && !event.metaKey && keys.has(event.key)) event.preventDefault();
    };
    grid.addEventListener("keydown", guard);
    return { dispose: () => grid.removeEventListener("keydown", guard) };
}
