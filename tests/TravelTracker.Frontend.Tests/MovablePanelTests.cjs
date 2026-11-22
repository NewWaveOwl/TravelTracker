// Run with node --test tests/TravelTracker.Frontend.Tests/MovablePanelTests.cjs.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const source = readFileSync(join(__dirname, "../../src/TravelTracker.Frontend/Components/Trips/MovablePanel.razor.js"), "utf8");
const modulePromise = import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

/** @return A minimal event surface that records preventDefault and registered handlers. */
function surface() {
    const handlers = new Map();
    return {
        handlers,
        addEventListener(name, handler) { handlers.set(name, handler); },
        removeEventListener(name) { handlers.delete(name); },
        fire(name, values = {}) {
            const event = { prevented: false, preventDefault() { this.prevented = true; }, ...values };
            handlers.get(name)?.(event);
            return event;
        }
    };
}

/** @return A controlled viewport, geometry and real movement controller without browser dependencies. */
async function setup() {
    const { attach } = await modulePromise;
    global.window = { ...surface(), innerWidth: 800, innerHeight: 600, visualViewport: { ...surface(), offsetLeft: 0, offsetTop: 0, width: 800, height: 600 } };
    let resize;
    let disconnected = false;
    global.ResizeObserver = class {
        constructor(callback) { resize = callback; }
        observe() { }
        disconnect() { disconnected = true; }
    };
    const handle = { ...surface(), capture: null, focus() { }, setPointerCapture(id) { this.capture = id; }, hasPointerCapture(id) { return this.capture === id; }, releasePointerCapture() { this.capture = null; } };
    const panel = {
        ...surface(), left: 100, top: 100, width: 240, height: 300,
        attributes: new Map(),
        setAttribute(name, value) { this.attributes.set(name, value); },
        removeAttribute(name) { this.attributes.delete(name); },
        style: { transform: "", removeProperty(name) { this[name] = ""; } },
        getBoundingClientRect() {
            const offset = this.style.transform.match(/translate\(([-\d.]+)px, ([-\d.]+)px\)/);
            return { left: this.left + Number(offset?.[1] ?? 0), top: this.top + Number(offset?.[2] ?? 0), width: this.width, height: this.height };
        }
    };
    return { panel, handle, controller: attach(panel, handle), resize: () => resize(), disconnected: () => disconnected };
}

test("keyboard moves within viewport, Shift increases distance and Home resets", async () => {
    const { panel, handle } = await setup();
    assert.equal(handle.fire("keydown", { key: "ArrowRight" }).prevented, true);
    assert.equal(panel.getBoundingClientRect().left, 110);
    handle.fire("keydown", { key: "ArrowDown", shiftKey: true });
    assert.equal(panel.getBoundingClientRect().top, 140);
    for (let index = 0; index < 30; index++) handle.fire("keydown", { key: "ArrowRight", shiftKey: true });
    assert.equal(panel.getBoundingClientRect().left, 552);
    assert.equal(handle.fire("keydown", { key: "Tab" }).prevented, false);
    handle.fire("keydown", { key: "Home" });
    assert.equal(panel.style.transform, "");
});

test("touch pointer capture clamps all edges and ends on cancellation", async () => {
    const { panel, handle } = await setup();
    handle.fire("pointerdown", { pointerId: 1, isPrimary: true, button: 0, pointerType: "touch", clientX: 100, clientY: 100 });
    assert.equal(handle.capture, 1);
    handle.fire("pointermove", { pointerId: 2, clientX: -1000, clientY: -1000 });
    assert.equal(panel.style.transform, "");
    handle.fire("pointermove", { pointerId: 1, clientX: -1000, clientY: -1000 });
    assert.equal(panel.getBoundingClientRect().left, 8);
    assert.equal(panel.getBoundingClientRect().top, 8);
    handle.fire("pointermove", { pointerId: 1, clientX: 2000, clientY: 2000 });
    assert.equal(panel.getBoundingClientRect().left, 552);
    assert.equal(panel.getBoundingClientRect().top, 292);
    handle.fire("pointercancel", { pointerId: 1 });
    assert.equal(handle.capture, null);
    assert.equal(panel.attributes.has("data-moving"), false);
});

test("content gestures have no movement handlers and secondary pointers are ignored", async () => {
    const { panel, handle } = await setup();
    assert.equal(panel.handlers.size, 0);
    assert.equal(handle.fire("pointerdown", { isPrimary: true, button: 2 }).prevented, false);
    assert.equal(handle.fire("pointerdown", { isPrimary: false, button: 0 }).prevented, false);
    assert.equal(handle.fire("keydown", { key: "ArrowDown", ctrlKey: true }).prevented, false);
    assert.equal(panel.style.transform, "");
});

test("moved handles stay below the sticky workspace save and reset controls", async () => {
    const { panel, handle } = await setup();
    panel.closest = () => ({ querySelector: () => ({ getBoundingClientRect: () => ({ bottom: 120 }) }) });
    handle.fire("keydown", { key: "ArrowUp", shiftKey: true });
    assert.equal(panel.getBoundingClientRect().top, 128);
    assert.equal(panel.style.maxHeight, "464px");
});

test("resize keeps moved handles reachable including oversized panels and visual viewport changes", async () => {
    const { panel, handle, resize } = await setup();
    handle.fire("keydown", { key: "ArrowRight" });
    window.visualViewport.width = 300;
    window.visualViewport.height = 200;
    window.visualViewport.offsetTop = 30;
    window.visualViewport.fire("resize");
    assert.equal(panel.getBoundingClientRect().left, 52);
    assert.equal(panel.getBoundingClientRect().top, 38);
    panel.width = 400;
    resize();
    assert.equal(panel.getBoundingClientRect().left, 8);
});

test("untouched below-fold mobile panels stay in document flow and disposal removes listeners", async () => {
    const { panel, handle, controller, resize, disconnected } = await setup();
    panel.top = 900;
    resize();
    assert.equal(panel.style.transform, "");
    handle.fire("keydown", { key: "ArrowUp" });
    controller.reset();
    assert.equal(panel.getBoundingClientRect().top, 900);
    controller.dispose();
    assert.equal(handle.handlers.size, 0);
    assert.equal(window.handlers.size, 0);
    assert.equal(window.visualViewport.handlers.size, 0);
    assert.equal(disconnected(), true);
});

for (const pointerType of ["mouse", "touch"]) {
    test(`two clean ${pointerType} taps reset the original grid position`, async () => {
        const { panel, handle } = await setup();
        handle.fire("keydown", { key: "ArrowRight" });
        for (const time of [100, 300]) {
            handle.fire("pointerdown", { pointerId: 1, isPrimary: true, button: 0, pointerType, clientX: 120, clientY: 100, timeStamp: time });
            handle.fire("pointerup", { pointerId: 1, pointerType, clientX: 120, clientY: 100, timeStamp: time + 40 });
        }
        assert.equal(panel.style.transform, "");
        assert.equal(panel.getBoundingClientRect().left, 100);
    });
}

for (const interruption of ["drag", "pointercancel", "lostpointercapture"]) {
    test(`${interruption} cannot complete or carry over a double tap`, async () => {
        const { panel, handle } = await setup();
        handle.fire("keydown", { key: "ArrowRight" });
        const down = time => handle.fire("pointerdown", { pointerId: 1, isPrimary: true, button: 0, pointerType: "touch", clientX: 120, clientY: 100, timeStamp: time });
        const up = time => handle.fire("pointerup", { pointerId: 1, pointerType: "touch", clientX: 120, clientY: 100, timeStamp: time });
        down(100); up(120);
        down(180);
        if (interruption === "drag") {
            handle.fire("pointermove", { pointerId: 1, clientX: 150, clientY: 100 });
            up(220);
        } else handle.fire(interruption, { pointerId: 1 });
        down(250); up(280);
        assert.notEqual(panel.style.transform, "");
    });
}
