export function viewBoxPoint(svg, clientX, clientY) {
    const point = new DOMPoint(clientX, clientY).matrixTransform(svg.getScreenCTM().inverse());
    return [point.x, point.y];
}

export function watchViewport(svg, map) {
    const observer = new ResizeObserver(([entry]) => {
        const { width, height } = entry.contentRect;
        map.invokeMethodAsync("ResizeViewport", Math.round(width), Math.round(height)).catch(() => {});
    });
    observer.observe(svg);
    const events = new AbortController();
    const options = { signal: events.signal };
    let pointer;
    let suppressClick = false;
    let offsetX = 0;
    let offsetY = 0;
    let sending = false;

    async function pan() {
        if (sending) return;
        sending = true;
        try {
            while (!events.signal.aborted && (offsetX !== 0 || offsetY !== 0)) {
                const x = offsetX;
                const y = offsetY;
                offsetX = offsetY = 0;
                await map.invokeMethodAsync("PanByDrag", x, y);
            }
        } catch {
            offsetX = offsetY = 0;
        } finally {
            sending = false;
        }
    }

    svg.addEventListener("pointerdown", event => {
        if (!event.isPrimary || event.button !== 0) return;
        suppressClick = false;
        if (event.target.closest(".marker-remove, .map-leg-label button")) return;
        const marker = event.target.closest(".trip-marker[data-move-index]");
        pointer = { id: event.pointerId, x: event.clientX, y: event.clientY, dragging: false, marker,
            transform: marker?.getAttribute("transform") };
    }, options);

    svg.addEventListener("pointermove", event => {
        if (pointer?.id !== event.pointerId) return;
        if (!pointer.dragging) {
            if (Math.hypot(event.clientX - pointer.x, event.clientY - pointer.y) < 5) return;
            pointer.dragging = true;
            suppressClick = true;
            svg.setPointerCapture(event.pointerId);
            svg.setAttribute("data-dragging", "");
        }
        const [x, y] = viewBoxPoint(svg, event.clientX, event.clientY);
        const [previousX, previousY] = viewBoxPoint(svg, pointer.x, pointer.y);
        if (pointer.marker) {
            pointer.marker.setAttribute("transform", pointer.transform + " translate(" + (x - previousX) + " " + (y - previousY) + ")");
            return;
        }
        pointer.x = event.clientX;
        pointer.y = event.clientY;
        offsetX += x - previousX;
        offsetY += y - previousY;
        void pan();
    }, options);

    function endDrag(event) {
        if (pointer?.id !== event.pointerId) return;
        const drag = pointer;
        pointer = undefined;
        svg.removeAttribute("data-dragging");
        if (!drag.marker || !drag.dragging) return;

        drag.marker.setAttribute("transform", drag.transform);
        if (event.type !== "pointerup") return;
        const [x, y] = viewBoxPoint(svg, event.clientX, event.clientY);
        const [startX, startY] = viewBoxPoint(svg, drag.x, drag.y);
        map.invokeMethodAsync("MoveMarker", Number(drag.marker.dataset.moveIndex), x - startX, y - startY).catch(() => {});
    }

    svg.addEventListener("pointerup", endDrag, options);
    svg.addEventListener("pointercancel", endDrag, options);
    svg.addEventListener("lostpointercapture", endDrag, options);
    svg.addEventListener("pointerleave", event => {
        if (!pointer?.dragging) endDrag(event);
    }, options);
    svg.addEventListener("click", event => {
        if (!suppressClick || event.detail === 0) return;
        suppressClick = false;
        event.stopPropagation();
    }, { ...options, capture: true });
    svg.addEventListener("keydown", event => {
        if (event.target === svg && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key))
            event.preventDefault();
    }, options);

    const markerType = "application/x-traveltracker-marker";
    svg.closest(".map-workspace")?.addEventListener("dragstart", event => {
        const marker = event.target.closest("button[data-map-marker]");
        if (!marker?.draggable || !event.dataTransfer) return;
        event.dataTransfer.setData(markerType, marker.dataset.mapMarker);
        event.dataTransfer.effectAllowed = "copy";
    }, options);
    svg.addEventListener("dragover", event => {
        if (!svg.dataset.markerDrop || !event.dataTransfer?.types.includes(markerType)) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "copy";
    }, options);
    svg.addEventListener("drop", event => {
        const type = event.dataTransfer?.getData(markerType);
        if (!svg.dataset.markerDrop || !type) return;
        event.preventDefault();
        const [x, y] = viewBoxPoint(svg, event.clientX, event.clientY);
        map.invokeMethodAsync("DropMarker", type, x, y).catch(() => {});
    }, options);

    return {
        disconnect() {
            events.abort();
            observer.disconnect();
        }
    };
}
