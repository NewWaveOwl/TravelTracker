# Phone controls and layout

## Design basis

The phone layout follows these primary sources:

- [W3C reflow guidance](https://www.w3.org/WAI/WCAG22/Understanding/reflow):
  ordinary page content should fit at 320 CSS pixels without horizontal scrolling.
  Maps may require two-dimensional interaction; adjacent controls and forms still
  need to fit.
- [W3C target size guidance](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum):
  WCAG 2.2 AA specifies 24 by 24 CSS pixels, subject to its exceptions. This app
  aims higher for primary phone buttons and map controls, using 48-pixel heights.
- [Google responsive accessibility guidance](https://web.dev/articles/accessible-responsive-design):
  comfortable tap targets, logical keyboard order and responsive content.

These changes improve phone usability; they do not certify full WCAG compliance.

## Implementation

The shared `mobile.css` loads after global and isolated component styles. Phone
rules apply at widths up to 700px, and on coarse-pointer screens at heights up to
500px to include landscape phones. Ordinary desktop layouts retain their styles.

- Map zoom, recenter and center-picking buttons use 48 by 48px targets. Short
  landscape screens arrange the toolbar horizontally.
- Buttons have a 48px minimum height, with explicit calendar exceptions. Calendar
  days remain at least 44px high and fit seven columns; the browser checks verify
  each cell is at least 24px wide at the tested sizes.
- Form actions wrap, trip panels use smaller padding, duration fields share
  available space, summary fields stack, and address actions form a single column.
- Text fields keep readable sizes and browser zoom remains enabled. Native form
  controls and the existing keyboard handlers are preserved.
- The viewport supports safe-area insets; page edges, menus and map controls
  account for device cutouts and bottom insets. Menus and catalogue dialogs have
  viewport-relative scrolling bounds.
- Movable panels start collapsed on portrait and short landscape phones. Existing
  tap, drag, keyboard, reset and field-preservation behavior stays in place.

## Automated browser verification

The browser regression test navigates Home, Account, Settings, History,
Attractions and Trip, checks document reflow, opens Map, checks control sizes,
opens and closes the calendar, checks all 42 day cells and calendar reflow, then
uses the phone menu to navigate to History. It verifies collapsed phone panels.
The sizes are 320x640, 390x844, 700x900, 844x390 and desktop 1280x900.

Run the Development frontend in one terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/TravelTracker.Frontend --configuration Release --no-launch-profile --urls http://localhost:5199
```

The test uses Playwright and an installed Edge browser. Install the test tool
outside the repository and run in a second terminal:

```powershell
npm install --prefix "$env:TEMP/traveltracker-mobile-tools" playwright --no-package-lock --no-audit --no-fund
$env:TRAVELTRACKER_PLAYWRIGHT_MODULE = "$env:TEMP/traveltracker-mobile-tools/node_modules/playwright"
node --test tests/TravelTracker.Frontend.Tests/MobileLayoutTests.cjs
```

Optional environment values: `TRAVELTRACKER_MOBILE_URL` selects another running
preview; `TRAVELTRACKER_BROWSER_CHANNEL` selects an installed Playwright-supported
browser channel; `TRAVELTRACKER_MOBILE_SCREENSHOTS` selects a local screenshot
directory. Do not commit screenshots containing personal data. These browser
tests are explicitly invoked and are separate from `dotnet test` and current CI.

## Recorded verification, 2026-11-23

- Before the change, the 320px browser regression failed on the existing 40x40px
  zoom button. After the change, all five browser cases passed.
- Full solution restore and Release build passed; build reported zero warnings
  and zero errors.
- Non-PostgreSQL application suite: 1,809 passed, zero failed or skipped.
- After the landscape/panel follow-up, Release build passed again and the frontend
  suite passed all 755 cases. Existing panel, map keyboard, duration and save
  JavaScript suites passed 35 cases, zero failures or skips.
- Portrait and landscape calendar screenshots were inspected. Browser automation
  used headless Edge with emulated viewport and touch support, not physical phones.

The preview had no live authentication or backend services. Authenticated settings,
picture uploads and catalogue dialogs therefore still need visual checks with
test accounts. Physical iOS/Safari, Android keyboards, real safe-area cutouts,
screen readers and pinch zoom remain manual verification boundaries. PostgreSQL
tests were not run because this change does not modify persistence.
