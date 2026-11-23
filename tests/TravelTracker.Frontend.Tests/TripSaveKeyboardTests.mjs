import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../../src/TravelTracker.Frontend/Components/Pages/NewTrip.razor.js", import.meta.url), "utf8");
const { watchSave } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
globalThis.Element = class {
    constructor(dialog = false) { this.dialog = dialog; }
    closest() { return this.dialog ? {} : null; }
    matches() { return true; }
    dispatchEvent() { this.onchange?.(); }
};

function setup({ disabled = false, connected = true, present = true, active, onSave } = {}) {
    let listener;
    let signal;
    let clicks = 0;
    active ??= new Element();
    globalThis.document = { body: new Element(), documentElement: new Element(), activeElement: active, addEventListener: (_, callback, options) => { listener = callback; signal = options.signal; } };
    const registration = watchSave({ isConnected: connected, contains: element => element === active, querySelector: selector =>
        selector === "[data-trip-save]" && present ? { disabled, click: () => { clicks++; onSave?.(); } } : null });
    return {
        send: (overrides = {}) => {
            let prevented = false;
            if (!signal.aborted) listener({ key: "s", ctrlKey: true, target: active,
                preventDefault: () => { prevented = true; }, ...overrides });
            return { prevented, clicks };
        },
        registration
    };
}

for (const modifier of ["ctrlKey", "metaKey"]) {
    test(`${modifier}+S saves while editing an input`, () => {
        const fixture = setup();
        assert.deepEqual(fixture.send({ ctrlKey: false, [modifier]: true }), { prevented: true, clicks: 1 });
    });
}

test("busy save suppresses the browser dialog without another submission", () => {
    assert.deepEqual(setup({ disabled: true }).send(), { prevented: true, clicks: 0 });
});

test("shortcut commits the focused field change before invoking save", () => {
    let boundValue = "Old name";
    const active = new Element();
    active.onchange = () => { boundValue = "Typed name"; };
    const fixture = setup({ active, onSave: () => assert.equal(boundValue, "Typed name") });
    assert.deepEqual(fixture.send(), { prevented: true, clicks: 1 });
});

test("held shortcut suppresses repeat submissions", () => {
    assert.deepEqual(setup().send({ repeat: true }), { prevented: true, clicks: 0 });
});

for (const [label, options, event] of [
    ["outside editor", { connected: false }, {}],
    ["while opening", { present: false }, {}],
    ["unmodified S", {}, { ctrlKey: false }],
    ["modal dialog", {}, { target: new Element(true) }],
    ["composition", {}, { isComposing: true }],
    ["handled event", {}, { defaultPrevented: true }]
]) {
    test(`${label} keeps native keyboard behavior`, () => {
        assert.deepEqual(setup(options).send(event), { prevented: false, clicks: 0 });
    });
}

test("leaving the editor removes the shortcut", () => {
    const fixture = setup();
    fixture.registration.disconnect();
    assert.deepEqual(fixture.send(), { prevented: false, clicks: 0 });
});
