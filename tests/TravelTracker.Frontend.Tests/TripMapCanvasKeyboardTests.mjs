import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../../src/TravelTracker.Frontend/Components/Trips/TripMapCanvas.razor.js", import.meta.url), "utf8");
const { watchViewport, watchContextLoss, hideExtrusions, lowQualityOptions } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

// Exercise the installed browser listener without downloading tiles or MapLibre.
globalThis.document = {
    createElement: () => ({}),
    head: { append() {} }
};
globalThis.ResizeObserver = class {
    observe() {}
    disconnect() {}
};

for (const selector of [".trip-marker", ".marker-remove"]) {
    test(`Space on ${selector} prevents page scrolling`, () => {
        const listeners = new Map();
        const svg = {
            addEventListener: (name, listener) => listeners.set(name, listener),
            closest: () => null
        };
        const viewport = watchViewport(svg, { replaceChildren() {} }, {}, [0, 0, 1]);
        try {
            let prevented = false;
            listeners.get("keydown")({
                key: " ",
                target: { closest: query => query.split(", ").includes(selector) ? {} : null },
                preventDefault: () => { prevented = true; }
            });
            assert.equal(prevented, true);
        } finally {
            viewport.disconnect();
        }
    });
}

test("Space on a native connection button retains native activation", () => {
    const listeners = new Map();
    const svg = {
        addEventListener: (name, listener) => listeners.set(name, listener),
        closest: () => null
    };
    const viewport = watchViewport(svg, { replaceChildren() {} }, {}, [0, 0, 1]);
    try {
        let prevented = false;
        listeners.get("keydown")({
            key: " ", target: { closest: () => null },
            preventDefault: () => { prevented = true; }
        });
        assert.equal(prevented, false);
    } finally {
        viewport.disconnect();
    }
});

test("low quality hides 3D buildings and terrain but leaves other layers alone", () => {
    const calls = [];
    const map = {
        setTerrain: terrain => calls.push(["terrain", terrain]),
        getStyle: () => ({ layers: [{ id: "water", type: "fill" }, { id: "buildings", type: "fill-extrusion" }, { id: "roads", type: "line" }] }),
        setLayoutProperty: (id, name, value) => calls.push([id, name, value])
    };

    hideExtrusions(map);

    assert.deepEqual(calls, [["terrain", null], ["buildings", "visibility", "none"]]);
});

test("low quality cuts pixel ratio, fades, pitch and antialiasing without turning off attribution", () => {
    assert.equal(lowQualityOptions.pixelRatio, 1);
    assert.equal(lowQualityOptions.fadeDuration, 0);
    assert.equal(lowQualityOptions.maxPitch, 0);
    assert.equal(lowQualityOptions.canvasContextAttributes.antialias, false);
    assert.equal("attributionControl" in lowQualityOptions, false);
});

test("a lost WebGL context calls the lost handler once", () => {
    const canvas = new EventTarget();
    let lost = 0;
    const originalError = console.error;
    console.error = () => {};
    try {
        watchContextLoss(canvas, () => lost++);
        canvas.dispatchEvent(new Event("webglcontextlost"));
        canvas.dispatchEvent(new Event("webglcontextlost"));
    } finally {
        console.error = originalError;
    }

    assert.equal(lost, 1);
});
