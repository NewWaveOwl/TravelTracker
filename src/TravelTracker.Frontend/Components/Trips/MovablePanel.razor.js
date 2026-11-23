/**
 * Moves and collapses an existing panel without recreating its editable child DOM.
 * @param {HTMLElement} panel The grid item to translate.
 * @param {HTMLButtonElement} handle The collapse, reset and keyboard movement control.
 * @param {HTMLElement} header The header accepting pointer movement gestures.
 * @return {{reset: Function, expand: Function, dispose: Function}} Layout, attention and lifecycle operations.
 */
export function attach(panel, handle, header = handle) {
    let x = 0, y = 0, drag = null, moved = false, lastTap = null, tapTimer = null;
    const body = panel.querySelector?.(".movable-panel-body");
    const narrow = window.matchMedia?.("(max-width: 700px), (max-height: 500px) and (pointer: coarse)");
    let collapsed = false;
    const clearTap = () => { clearTimeout(tapTimer); tapTimer = null; lastTap = null; };
    const collapse = value => {
        collapsed = value;
        if (value) panel.setAttribute("data-collapsed", "");
        else panel.removeAttribute("data-collapsed");
        handle.setAttribute?.("aria-expanded", String(!value));
        if (body) { body.hidden = value; body.inert = value; }
    };
    collapse(narrow?.matches ?? false);
    panel.setAttribute("data-panel-ready", "");
    const viewport = window.visualViewport;
    const bounds = () => ({
        left: (viewport?.offsetLeft ?? 0) + 8,
        top: Math.max((viewport?.offsetTop ?? 0) + 8,
            (panel.closest?.(".trip-planner")?.getBoundingClientRect?.().top ?? 0) + 8),
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
        clearTap();
        collapse(false);
        x = y = 0;
        moved = false;
        panel.style.removeProperty("transform");
        panel.style.removeProperty("max-height");
    };
    const down = event => {
        if (drag || !event.isPrimary || event.button !== 0) return;
        if (event.target && event.target !== handle && !handle.contains?.(event.target) &&
            event.target.closest?.("button, a, input, select, textarea")) return;
        clearTimeout(tapTimer);
        tapTimer = null;
        event.preventDefault();
        handle.focus({ preventScroll: true });
        drag = { id: event.pointerId, x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, time: event.timeStamp, travelled: false, type: event.pointerType, toggle: event.target ? event.target === handle || handle.contains?.(event.target) : true };
        handle.setPointerCapture(event.pointerId);
        panel.setAttribute("data-moving", "");
    };
    const pointerMove = event => {
        if (!drag || drag.id !== event.pointerId) return;
        if (Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > 5) {
            drag.travelled = true;
            clearTap();
        }
        if (!drag.travelled) return;
        move(event.clientX - drag.x, event.clientY - drag.y);
        drag.x = event.clientX;
        drag.y = event.clientY;
    };
    const up = event => {
        if (drag?.id !== event.pointerId) return;
        const active = drag;
        end();
        if (active.travelled || event.timeStamp - active.time > 500 ||
            Math.hypot(event.clientX - active.startX, event.clientY - active.startY) > 5) {
            clearTap();
            return;
        }
        if (!active.toggle) { clearTap(); return; }
        if (lastTap && event.pointerType === lastTap.type && event.timeStamp - lastTap.time <= 350 &&
            Math.hypot(event.clientX - lastTap.x, event.clientY - lastTap.y) <= 20) reset();
        else {
            lastTap = { time: event.timeStamp, x: event.clientX, y: event.clientY, type: event.pointerType };
            tapTimer = setTimeout(() => { clearTap(); collapse(!collapsed); clamp(); }, 350);
        }
    };
    const cancel = event => {
        if (drag?.id === event.pointerId) { clearTap(); end(); }
    };
    const key = event => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const step = event.shiftKey ? 40 : 10;
        const offsets = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
        if (event.key === "Home") { event.preventDefault(); reset(); }
        else if (offsets[event.key]) { event.preventDefault(); clearTap(); end(); move(...offsets[event.key]); }
    };
    // Do not pull untouched mobile grid items above the fold into the viewport.
    const clamp = () => { if (moved) move(0, 0); };
    // Pointer clicks are resolved above; a synthetic native click remains available to keyboard and assistive technology.
    const click = event => { if (event.detail === 0) { clearTap(); collapse(!collapsed); clamp(); } };
    const breakpoint = event => { clearTap(); collapse(event.matches); clamp(); };
    handle.addEventListener("click", click);
    narrow?.addEventListener("change", breakpoint);
    header.addEventListener("pointerdown", down);
    header.addEventListener("pointermove", pointerMove);
    header.addEventListener("pointerup", up);
    header.addEventListener("pointercancel", cancel);
    handle.addEventListener("lostpointercapture", cancel);
    handle.addEventListener("keydown", key);
    window.addEventListener("resize", clamp);
    viewport?.addEventListener("resize", clamp);
    viewport?.addEventListener("scroll", clamp);
    const observer = new ResizeObserver(clamp);
    observer.observe(panel);
    return {
        reset,
        expand() { clearTap(); collapse(false); clamp(); },
        dispose() {
            clearTap();
            end();
            collapse(false);
            panel.removeAttribute("data-panel-ready");
            observer.disconnect();
            handle.removeEventListener("click", click);
            narrow?.removeEventListener("change", breakpoint);
            header.removeEventListener("pointerdown", down);
            header.removeEventListener("pointermove", pointerMove);
            header.removeEventListener("pointerup", up);
            header.removeEventListener("pointercancel", cancel);
            handle.removeEventListener("lostpointercapture", cancel);
            handle.removeEventListener("keydown", key);
            window.removeEventListener("resize", clamp);
            viewport?.removeEventListener("resize", clamp);
            viewport?.removeEventListener("scroll", clamp);
        }
    };
}
