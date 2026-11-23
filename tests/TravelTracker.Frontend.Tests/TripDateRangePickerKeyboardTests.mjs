import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
const source = await readFile(new URL("../../src/TravelTracker.Frontend/Components/Trips/TripDateRangePicker.razor.js", import.meta.url), "utf8");
const { attach } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
for (const [key, prevented] of [["ArrowDown", true], ["PageUp", true], [" ", true], ["Tab", false], ["Escape", false], ["a", false]]) {
    test(`calendar ${key} ${prevented ? "suppresses page scrolling" : "retains native behavior"}`, () => {
        let listener;
        const grid = { addEventListener: (_, action) => listener = action, removeEventListener: () => listener = null };
        const controller = attach(grid);
        let actual = false;
        listener({ key, preventDefault: () => actual = true });
        assert.equal(actual, prevented);
        controller.dispose();
        assert.equal(listener, null);
    });
}
