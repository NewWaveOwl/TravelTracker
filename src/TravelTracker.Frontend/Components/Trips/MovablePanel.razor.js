/**
 * Moves an existing grid item through its handle without recreating its child DOM.
 * @param {HTMLElement} panel The grid item to translate.
 * @param {HTMLButtonElement} handle The only element accepting movement gestures.
 * @return {{reset: Function, dispose: Function}} Layout and lifecycle operations.
 */
export function attach(panel, handle) {
    let x = 0, y = 0, drag = null, moved = false;
    const viewport = window.visualViewport;
    const bounds = () => ({
        left: (viewport?.offsetLeft ?? 0) + 8,
        top: Math.max((viewport?.offsetTop ?? 0) + 8,
            (panel.closest?.(".trip-planner")?.querySelector(".trip-workspace-actions")?.getBoundingClientRect().bottom ?? 0) + 8),
        right: (viewport?.offsetLeft ?? 0) + (viewport?.width ?? window.innerWidth) - 8,
        bottom: (viewport?.offsetTop ?? 0) + (viewport?.height ?? window.innerHeight) - 8
    });
    const render = () => { panel.style.transform = `translate(${x}px, ${y}px)`; };
    const move = (dx, dy) => {
        const area = bounds();
        panel.style.maxHeight = `${Math.max(44, area.bottom - area.top)}px`;
        const rect = panel.getBoundingClientRect();
        // For a panel larger than the visual viewport, keep its handle reachable.
        const right = Math.max(area.left, area.right - rect.width);
        const bottom = Math.max(area.top, area.bottom - rect.height);
        const left = Math.min(right, Math.max(area.left, rect.left + dx));
        const top = Math.min(bottom, Math.max(area.top, rect.top + dy));
        x += left - rect.left;
        y += top - rect.top;
        moved = true;
        render();
    };
    const end = () => {
        const active = drag;
        drag = null;
        panel.removeAttribute("data-moving");
        if (active && handle.hasPointerCapture(active.id)) handle.releasePointerCapture(active.id);
    };
    const reset = () => {
        end();
        x = y = 0;
        moved = false;
        panel.style.removeProperty("transform");
        panel.style.removeProperty("max-height");
    };
    const down = event => {
        if (drag || !event.isPrimary || event.button !== 0) return;
        event.preventDefault();
        handle.focus({ preventScroll: true });
        drag = { id: event.pointerId, x: event.clientX, y: event.clientY };
        handle.setPointerCapture(event.pointerId);
        panel.setAttribute("data-moving", "");
    };
    const pointerMove = event => {
        if (!drag || drag.id !== event.pointerId) return;
        move(event.clientX - drag.x, event.clientY - drag.y);
        drag.x = event.clientX;
        drag.y = event.clientY;
    };
    const up = event => { if (drag?.id === event.pointerId) end(); };
    const key = event => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const step = event.shiftKey ? 40 : 10;
        const offsets = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
        if (event.key === "Home") { event.preventDefault(); reset(); }
        else if (offsets[event.key]) { event.preventDefault(); end(); move(...offsets[event.key]); }
    };
    // Do not pull untouched mobile grid items above the fold into the viewport.
    const clamp = () => { if (moved) move(0, 0); };
    handle.addEventListener("pointerdown", down);
    handle.addEventListener("pointermove", pointerMove);
    handle.addEventListener("pointerup", up);
    handle.addEventListener("pointercancel", up);
    handle.addEventListener("lostpointercapture", up);
    handle.addEventListener("keydown", key);
    window.addEventListener("resize", clamp);
    viewport?.addEventListener("resize", clamp);
    viewport?.addEventListener("scroll", clamp);
    const observer = new ResizeObserver(clamp);
    observer.observe(panel);
    return {
        reset,
        dispose() {
            end();
            observer.disconnect();
            handle.removeEventListener("pointerdown", down);
            handle.removeEventListener("pointermove", pointerMove);
            handle.removeEventListener("pointerup", up);
            handle.removeEventListener("pointercancel", up);
            handle.removeEventListener("lostpointercapture", up);
            handle.removeEventListener("keydown", key);
            window.removeEventListener("resize", clamp);
            viewport?.removeEventListener("resize", clamp);
            viewport?.removeEventListener("scroll", clamp);
        }
    };
}
