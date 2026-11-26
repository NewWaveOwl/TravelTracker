import { readFile } from "node:fs/promises";
import assert from "node:assert/strict";
import test from "node:test";

const source = await readFile(new URL("../../../src/TravelTracker.Frontend/Components/Pages/NewTrip.razor.js", import.meta.url), "utf8");
const { watchSave, watchEditorShortcuts } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

/** Minimal DOM surface for exercising native event cancellation without browser or framework dependencies. */
class EditorElement extends EventTarget {
    constructor(parent = null, kind = "div") {
        super();
        this.parent = parent;
        this.kind = kind;
        this.isConnected = true;
        this.disabled = false;
        this.dialog = false;
        this.isContentEditable = false;
    }
    contains(element) { return element === this || element?.parent && this.contains(element.parent); }
    closest(selector) {
        if (selector.includes("role=dialog") && this.dialog) return this;
        return this.parent?.closest(selector) ?? null;
    }
    matches() { return ["input", "textarea", "select"].includes(this.kind); }
}

/**
 * Builds one isolated mounted editor and records field changes and button activations.
 * @return The root, controls and an event dispatcher shared by each behavior case.
 */
function fixture() {
    globalThis.Element = EditorElement;
    const document = new EventTarget();
    document.body = new EditorElement();
    document.documentElement = new EditorElement();
    globalThis.document = document;
    const root = new EditorElement(document.body);
    const input = new EditorElement(root, "input");
    const button = new EditorElement(root, "button");
    const undo = new EditorElement(root, "button");
    const calls = [];
    button.click = () => calls.push("save");
    const remove = new EditorElement(root, "button");
    remove.click = () => calls.push("delete");
    undo.click = () => calls.push("undo");
    input.addEventListener("change", () => calls.push("change"));
    root.querySelector = selector => selector === "[data-trip-save]" ? button
        : selector === "[data-trip-delete]:enabled" ? remove
        : selector === "[data-trip-undo]:enabled" && !undo.disabled ? undo : null;
    return {
        root, input, button, undo, calls, document,
        key(target = input, options = {}) {
            const event = new Event("keydown", { cancelable: true, bubbles: true });
            for (const [key, value] of Object.entries({ key: "s", ctrlKey: true, metaKey: false,
                altKey: false, shiftKey: false, repeat: false, isComposing: false, target, ...options }))
                Object.defineProperty(event, key, { value });
            document.dispatchEvent(event);
            return event;
        }
    };
}

for (const modifier of ["ctrlKey", "metaKey"]) {
    test(`${modifier}+S cancels native Save Page and commits the focused field before the same Save button`, () => {
        const editor = fixture();
        const registration = watchSave(editor.root);
        assert.equal(editor.key(editor.input, { ctrlKey: false, [modifier]: true }).defaultPrevented, true);
        assert.deepEqual(editor.calls, ["change", "save"]);
        registration.disconnect();
    });
}

test("keyboard save works when the map has page focus", () => {
    const editor = fixture();
    const registration = watchSave(editor.root);
    assert.equal(editor.key(editor.document.body).defaultPrevented, true);
    assert.deepEqual(editor.calls, ["save"]);
    registration.disconnect();
});

test("a disabled button and held shortcut suppress the browser without duplicate save", () => {
    const editor = fixture();
    const registration = watchSave(editor.root);
    editor.button.disabled = true;
    assert.equal(editor.key().defaultPrevented, true);
    editor.button.disabled = false;
    assert.equal(editor.key(editor.input, { repeat: true }).defaultPrevented, true);
    assert.deepEqual(editor.calls, []);
    registration.disconnect();
});

test("events outside the editor and inside a dialog retain browser behavior", () => {
    const editor = fixture();
    const registration = watchSave(editor.root);
    assert.equal(editor.key(new EditorElement()).defaultPrevented, false);
    editor.input.dialog = true;
    assert.equal(editor.key().defaultPrevented, false);
    assert.deepEqual(editor.calls, []);
    registration.disconnect();
});

for (const [name, options] of Object.entries({
    "plain S": { ctrlKey: false }, "Alt+Ctrl+S": { altKey: true }, "Shift+Ctrl+S": { shiftKey: true },
    "IME composition": { isComposing: true }, "another key": { key: "p" }
})) {
    test(`${name} retains normal browser behavior`, () => {
        const editor = fixture();
        const registration = watchSave(editor.root);
        assert.equal(editor.key(editor.input, options).defaultPrevented, false);
        assert.deepEqual(editor.calls, []);
        registration.disconnect();
    });
}

test("already handled events are left alone", () => {
    const editor = fixture();
    editor.document.addEventListener("keydown", event => event.preventDefault());
    const registration = watchSave(editor.root);
    assert.equal(editor.key().defaultPrevented, true);
    assert.deepEqual(editor.calls, []);
    registration.disconnect();
});

test("navigation and disposal release save interception", () => {
    const editor = fixture();
    const registration = watchSave(editor.root);
    editor.root.isConnected = false;
    assert.equal(editor.key().defaultPrevented, false);
    editor.root.isConnected = true;
    registration.disconnect();
    assert.equal(editor.key().defaultPrevented, false);
    assert.deepEqual(editor.calls, []);
});

test("a loading editor without a Save action leaves browser behavior alone", () => {
    const editor = fixture();
    editor.root.querySelector = () => null;
    const registration = watchSave(editor.root);
    assert.equal(editor.key().defaultPrevented, false);
    assert.deepEqual(editor.calls, []);
    registration.disconnect();
});

test("combined shortcut cleanup keeps the existing undo path and releases both handlers", () => {
    const editor = fixture();
    const registration = watchEditorShortcuts(editor.root);
    assert.equal(editor.key(editor.root, { key: "z" }).defaultPrevented, true);
    assert.equal(editor.key(editor.root).defaultPrevented, true);
    assert.deepEqual(editor.calls, ["undo", "save"]);
    registration.disconnect();
    assert.equal(editor.key(editor.root, { key: "z" }).defaultPrevented, false);
    assert.equal(editor.key(editor.root).defaultPrevented, false);
});

test("Delete activates only the selected item and preserves native text editing", () => {
    const editor = fixture();
    const registration = watchEditorShortcuts(editor.root);
    const press = (target, options = {}) => editor.key(target, { key: "Delete", ctrlKey: false, ...options }).defaultPrevented;
    assert.equal(press(editor.input), false);
    assert.equal(press(new EditorElement()), false);
    assert.equal(press(editor.root, { repeat: true }), false);
    assert.equal(press(editor.root, { shiftKey: true }), false);
    assert.equal(press(editor.root), true);
    assert.deepEqual(editor.calls, ["delete"]);
    registration.disconnect();
    assert.equal(press(editor.root), false);
});
