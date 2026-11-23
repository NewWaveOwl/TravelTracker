// Run against the Development preview; see docs/milestone-4/phone-layout.md.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { mkdirSync } = require('node:fs');
const { join } = require('node:path');
const { chromium } = require(process.env.TRAVELTRACKER_PLAYWRIGHT_MODULE || 'playwright');

const baseUrl = process.env.TRAVELTRACKER_MOBILE_URL || 'http://localhost:5199';
const sizes = [[320, 640], [390, 844], [700, 900], [844, 390], [1280, 900]];

/**
 * Checks real layout dimensions rather than relying on stylesheet text.
 * @param page The browser page under test.
 * @return Completion of document reflow verification.
 */
async function assertReflow(page) {
    const dimensions = await page.evaluate(() => ({ viewport: innerWidth, content: document.documentElement.scrollWidth }));
    assert.ok(dimensions.content <= dimensions.viewport + 1, JSON.stringify(dimensions));
}

for (const [width, height] of sizes) {
    test(`Navigation, map controls and calendar at ${width}x${height}`, async () => {
        const browser = await chromium.launch({ channel: process.env.TRAVELTRACKER_BROWSER_CHANNEL || 'msedge', headless: true });
        const context = await browser.newContext({ viewport: { width, height }, isMobile: width < 1000, hasTouch: true });
        const page = await context.newPage();
        try {
            for (const route of ['/', '/account', '/settings', '/history', '/attractions', '/trip']) {
                await page.goto(baseUrl + route);
                await assertReflow(page);
            }
            await page.goto(baseUrl + '/map');
            await page.locator('.movable-panel[data-panel-ready]').first().waitFor();
            await assertReflow(page);

            const controls = await page.locator('.map-controls button').evaluateAll(elements => elements.map(element => {
                const bounds = element.getBoundingClientRect();
                return { label: element.getAttribute('aria-label'), width: bounds.width, height: bounds.height };
            }));
            for (const control of controls) {
                const minimum = width <= 700 || height <= 500 ? 48 : 40;
                assert.ok(control.width >= minimum && control.height >= minimum, JSON.stringify(control));
            }
            const handle = page.locator('.movable-panel-handle').first();
            const phone = width <= 700 || height <= 500;
            if (phone) assert.equal(await handle.getAttribute('aria-expanded'), 'false', 'Phone panels start collapsed');
            if (await handle.getAttribute('aria-expanded') === 'false') {
                await handle.tap();
                await page.waitForFunction(() => document.querySelector('.movable-panel-handle')?.getAttribute('aria-expanded') === 'true');
            }
            await page.getByRole('button', { name: /Travel dates/ }).click();
            await page.locator('.range-calendar').waitFor();
            const calendar = await page.locator('.range-calendar').evaluate(element => ({
                width: element.clientWidth, content: element.scrollWidth,
                days: [...element.querySelectorAll('.calendar-day')].map(day => {
                    const bounds = day.getBoundingClientRect();
                    return { width: bounds.width, height: bounds.height };
                })
            }));
            assert.ok(calendar.content <= calendar.width + 1, 'Calendar must not scroll horizontally');
            assert.equal(calendar.days.length, 42);
            for (const day of calendar.days) assert.ok(day.width >= 24 && day.height >= 44, JSON.stringify(day));
            await assertReflow(page);
            if (process.env.TRAVELTRACKER_MOBILE_SCREENSHOTS) {
                mkdirSync(process.env.TRAVELTRACKER_MOBILE_SCREENSHOTS, { recursive: true });
                await page.screenshot({ path: join(process.env.TRAVELTRACKER_MOBILE_SCREENSHOTS, `map-${width}x${height}.png`) });
            }
            await page.getByRole('button', { name: 'Close calendar', exact: true }).click();
            await page.locator('.range-calendar').waitFor({ state: 'detached' });
            if (phone) {
                await handle.tap();
                await page.waitForFunction(() => document.querySelector('.movable-panel-handle')?.getAttribute('aria-expanded') === 'false');
                await page.getByRole('button', { name: 'Menu', exact: true }).tap();
                const menu = page.getByRole('navigation', { name: 'More navigation' });
                await menu.waitFor();
                const bounds = await menu.boundingBox();
                assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1);
                assert.ok(bounds.y + bounds.height <= height + 1, 'Menu stays inside the screen');
                await menu.getByRole('link', { name: 'History', exact: true }).tap();
                await page.waitForURL('**/history');
                await menu.waitFor({ state: 'detached' });
            }
        } finally {
            await context.close();
            await browser.close();
        }
    });
}
