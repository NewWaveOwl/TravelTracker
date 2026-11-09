const mapLibreDist = "https://unpkg.com/maplibre-gl@6.17.0/dist/";
const basemapStyle = "https://tiles.openfreemap.org/styles/liberty";
let mapLibreAssets;

// SHA-256 digests for the pinned release, including the separately loaded worker.
const mapLibreIntegrity = {
    "maplibre-gl.css": "sha256-qQeYoZUwF3kOWQ9+G9Nf8QOopCY5cstHKQt4rfhvjCA=",
    "maplibre-gl.mjs": "sha256-4CywjFcv0JUKdTQ81VgJnCodu69n8msMlvEVQxli0a4=",
    "maplibre-gl-worker.mjs": "sha256-c9egJKTv4aRuSdsZPhjP4zJx4+zGUux1QsKBpO9fDwQ="
};

async function fetchVerifiedModule(name) {
    const response = await fetch(mapLibreDist + name, { integrity: mapLibreIntegrity[name], credentials: "omit" });
    if (!response.ok) throw new Error(`MapLibre asset unavailable: ${name}`);
    return response.blob();
}

async function loadVerifiedModules() {
    const [module, worker] = await Promise.all([
        fetchVerifiedModule("maplibre-gl.mjs"),
        fetchVerifiedModule("maplibre-gl-worker.mjs")
    ]);
    const moduleUrl = URL.createObjectURL(module);
    try {
        const library = await import(moduleUrl);
        // MapLibre's default worker URL is relative to the main module, which is now a blob.
        library.setWorkerUrl(URL.createObjectURL(worker));
        return library;
    } finally {
        URL.revokeObjectURL(moduleUrl);
    }
}

function loadMapLibre() {
    if (!mapLibreAssets) {
        const stylesheet = document.createElement("link");
        stylesheet.rel = "stylesheet";
        stylesheet.href = mapLibreDist + "maplibre-gl.css";
        stylesheet.integrity = mapLibreIntegrity["maplibre-gl.css"];
        stylesheet.crossOrigin = "anonymous";
        const css = new Promise((resolve, reject) => {
            stylesheet.onload = resolve;
            stylesheet.onerror = reject;
        });
        document.head.append(stylesheet);

        mapLibreAssets = css.then(loadVerifiedModules)
            .catch(error => {
                stylesheet.remove();
                mapLibreAssets = undefined;
                throw error;
            });
    }
    return mapLibreAssets;
}

function attachBasemap(container, planner, initialCamera) {
    let basemap;
    let camera = initialCamera;
    let attempt = 0;
    let timeout;

    function clearMap() {
        clearTimeout(timeout);
        basemap?.remove();
        basemap = undefined;
        container.replaceChildren();
    }

    function notify(ready) {
        planner.invokeMethodAsync("SetBasemapReady", ready).catch(() => {});
    }

    async function load() {
        const generation = ++attempt;
        clearMap();

        function fail() {
            if (generation !== attempt) return;
            attempt++;
            clearMap();
            notify(false);
        }

        timeout = setTimeout(fail, 15000);
        try {
            const { Map } = await loadMapLibre();
            if (generation !== attempt) return;

            const [longitude, latitude, zoom] = camera;
            basemap = new Map({
                container,
                style: basemapStyle,
                center: [longitude, latitude],
                zoom,
                interactive: false,
                trackResize: false
            });
            const canvas = basemap.getCanvas();
            canvas.tabIndex = -1;
            canvas.setAttribute("aria-hidden", "true");
            basemap.once("load", () => {
                if (generation !== attempt) return;
                clearTimeout(timeout);
                notify(true);
            });
        } catch {
            fail();
        }
    }

    void load();
    return {
        setCamera(next) {
            camera = next;
            const [longitude, latitude, zoom] = camera;
            basemap?.jumpTo({ center: [longitude, latitude], zoom });
        },
        resize() { basemap?.resize(); },
        retry() { void load(); },
        disconnect() {
            attempt++;
            clearMap();
        }
    };
}

export function viewBoxPoint(svg, clientX, clientY) {
    const point = new DOMPoint(clientX, clientY).matrixTransform(svg.getScreenCTM().inverse());
    return [point.x, point.y];
}

export function watchViewport(svg, container, map, camera) {
    const basemap = attachBasemap(container, map, camera);
    const observer = new ResizeObserver(([entry]) => {
        basemap.resize();
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
    let wheelDelta = 0;
    let lastWheelTime = -Infinity;

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

    svg.addEventListener("wheel", event => {
        const mode = event.deltaMode;
        const { deltaX, deltaY } = event;
        if (event.ctrlKey || event.metaKey || !deltaY || Math.abs(deltaX) > Math.abs(deltaY)) return;

        event.preventDefault();
        if (pointer?.dragging) return;

        // Collect small trackpad deltas and treat line/page wheel events as a step.
        const delta = mode === 0 ? deltaY : Math.sign(deltaY) * 60;
        if (event.timeStamp - lastWheelTime > 250 || Math.sign(delta) !== Math.sign(wheelDelta))
            wheelDelta = 0;
        lastWheelTime = event.timeStamp;
        wheelDelta += delta;
        if (Math.abs(wheelDelta) < 60) return;

        wheelDelta = 0;
        const [x, y] = viewBoxPoint(svg, event.clientX, event.clientY);
        map.invokeMethodAsync("ZoomAtPointer", -Math.sign(delta), x, y).catch(() => {});
    }, { ...options, passive: false });

    svg.addEventListener("pointerdown", event => {
        const rightMouse = event.pointerType === "mouse" && event.button === 2;
        if (!event.isPrimary || (event.button !== 0 && !rightMouse)) return;
        suppressClick = false;
        if (!rightMouse && event.target.closest(".marker-remove, .map-leg-label button")) return;

        const marker = rightMouse ? null : event.target.closest(".trip-marker[data-move-index]");
        const canPan = rightMouse || event.pointerType !== "mouse" || !event.target.closest(".location-target");
        pointer = { id: event.pointerId, x: event.clientX, y: event.clientY, dragging: false, marker, canPan,
            transform: marker?.getAttribute("transform") };
        // Keep receiving movement immediately, even if the pointer leaves a small pin before the drag threshold.
        // Capture on the pin itself so a normal click still selects that stop.
        marker?.setPointerCapture(event.pointerId);
    }, options);

    svg.addEventListener("contextmenu", event => {
        if (event.button === 2) event.preventDefault();
    }, options);

    svg.addEventListener("pointermove", event => {
        if (pointer?.id !== event.pointerId) return;
        if (!pointer.dragging) {
            if (Math.hypot(event.clientX - pointer.x, event.clientY - pointer.y) < 5) return;
            pointer.dragging = true;
            suppressClick = true;
            if (!pointer.marker) svg.setPointerCapture(event.pointerId);
            if (pointer.canPan || pointer.marker) svg.setAttribute("data-dragging", "");
        }
        if (!pointer.canPan && !pointer.marker) return;

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
        if (!pointer?.dragging && !pointer?.marker) endDrag(event);
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
    svg.addEventListener("dragstart", event => {
        if (event.target.closest(".trip-marker[data-move-index]")) event.preventDefault();
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
        setCamera: basemap.setCamera,
        retryBasemap: basemap.retry,
        disconnect() {
            events.abort();
            observer.disconnect();
            basemap.disconnect();
        }
    };
}
