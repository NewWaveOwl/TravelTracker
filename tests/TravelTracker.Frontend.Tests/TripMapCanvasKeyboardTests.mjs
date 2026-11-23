import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../../src/TravelTracker.Frontend/Components/Trips/TripMapCanvas.razor.js", import.meta.url), "utf8");
const { watchViewport } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

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
