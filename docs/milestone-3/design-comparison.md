# Design comparison for the settings and trip views (#47, #48)

Compared on 2026-11-08 by reading the Figma nodes (design context, metadata and screenshots) against the Blazor markup and `wwwroot/styles.css`. It is not a pixel comparison of a running browser, so spacing and font rendering are unverified.

## Frames used

- Settings: [TT V4 / Settings / B · Grouped preferences](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=136-1133).
- Settings states: [save confirmation](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-4799), [failed save](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-4938) and [expired session](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-5077).
- Trip views have no dedicated frame. They follow the reusable pages: [03 Reusable form page](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3488), [06 Validation error](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3509), [04 Loading](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3495), [07 Permission denied](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3516), [08 Network error](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3523) and [09 Service unavailable](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3530). Place selection follows the [Issue 44 side-by-side concept](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=119-1028).

## Settings (#48)

Matches the frame: "Settings" heading, the intro line, "App preferences" (theme, time zone) beside "Trip preferences", and a "Save changes" button.

Differs on purpose, because the Settings service contract wins over the frame (recorded in #85):

- no Language field, since the service has no language setting.
- the "Default map view" dropdown is numeric latitude, longitude and zoom fields.
- "Preferred transport mode" is shown with a label. The frame has an unlabelled empty slot for it.
- each field carries a short hint saying whether the value is applied yet.

Fixed in this branch: error panels, field errors and invalid input borders were red. The validation frame uses amber (text #845600 on #f7d58b), so they now use amber on the project's hsl grid. This covers the settings page and the trip views.

## Trip views (#47)

- The error summary, field errors, loading, retry and permission wording follow the state frames. The summary lists links to the fields instead of the frame's single bullet-separated line, so each error can be reached from the keyboard.
- The frames use Aldrich and Atkinson Hyperlegible. The project rule allows generic font families only, so the app inherits the shared page fonts.

## Not changed (outside this work)

The shared `Feedback` component and the Attractions and Accommodations pages still differ from the state frames:

- Permission denied in the frame has a "RESTRICTED" label, the title "Access is restricted" and a "Back to start" button. `Feedback` shows "Permission denied" with no label or button.
- `Feedback` errors and the Accommodations error panels are red. The frames use amber.

Those belong to the shell (M3-06) and #46. Changing them would restyle every page, so they are listed here for the owners.
