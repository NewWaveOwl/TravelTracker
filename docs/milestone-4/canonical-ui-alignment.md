# Canonical UI alignment

The implementation reference is the owner's updated TravelTracker Figma design,
checked on 2026-11-22:

- [Current screens and states](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3446).
- [Shared components, assets and interaction rules](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=240-3538).

These references supersede the older visual targets recorded in
[the previous alignment review](figma-alignment-review.md). The Figma document
was read through the official Figma tools and was not edited. Page-level links
identify canvases; their child screen and component frames provide the
high-fidelity design context and screenshots used for implementation.

## Reference coverage

| Area | Canonical frames |
| --- | --- |
| Home, sign-in and registration | `231:3239`, `231:3286`, `231:3336` |
| Trip basics, itinerary and review | `147:3782`, `147:3919`, `147:4063` |
| Map and saved trip | `232:3349`, `147:4191` |
| History and empty history | `232:3553`, `160:2833` |
| Attractions and accommodation catalogues | `147:3105`, `147:3211`, `147:3364`, `147:3488`, `147:3619` |
| Profile and settings | `143:2028`, `143:2141`, `150:2599`, `136:1133` |
| Administration and service status | `160:2983`, `160:3039`, `160:2933`, `160:2684` |
| Feedback and confirmations | `92:3495`, `92:3502`, `92:3509`, `92:3516`, `92:3523`, `92:3530`, `92:3701`, `147:4596`, `147:4465`, `160:2735`, `160:2784` |
| Shared rules and floating panels | `247:55`, `257:1550`, `257:1551`, `257:1624`, `257:1625`, `257:1660`, `257:1661`, `232:3340` |

## Design rules

The shared palette uses forest `#3e4f3d`, primary `#436046`, warm background
`#f0eadf`, white surfaces and peach `#ffcabe` selection. Invalid user input uses
amber text `#845600` on warm yellow `#f7d58b`. API, network and save failures use
red `#b3261e` on `#fce8e6`; a permission state is neutral. Text and recovery
actions distinguish the states without relying on colour alone.

Aldrich remains the heading/action font and Atkinson Hyperlegible the content
font. The desktop reference has a 120-pixel header and a centred 1104-pixel
reading/form column. Catalogues use wider workspaces; mobile layouts stack
content and keep controls reachable.

The Map panel box icon has three distinct interactions: a single click toggles
the panel, a double-click or double-tap restores its original position and
expands it, and dragging moves it. A completed drag suppresses the click action.
Collapsing a panel preserves its editor state. The map's New trip and Save trip
icons stay at the top right with tooltips and accessible names. New trip retains
the unsaved-work confirmation. Save, review and new feedback reveal the details
panel at its current position so a collapsed panel cannot hide validation,
load/save failures or confirmation messages.

## Working behavior takes precedence over sample data

The reference's Moravia itinerary, addresses, transport names and route times
are sample data. The app continues to display the traveller's actual draft and
live basemap. The map screenshot is not an application background. The UI does
not claim road routing, reverse geocoding, real-time transit or deletion that
the current API does not implement.

The existing date-range calendar, editable coordinates, HTTP-only service
boundary, authentication and ownership checks remain part of the application.
Profile credential forms and other implemented controls absent from an
illustrative frame remain available within the same component styling.

## Verification

The frontend baseline was 498 tests discovered and passed, with no failures or
skips. Final checks on 2026-11-22:

- `dotnet restore TravelTracker.slnx`: passed.
- `dotnet build TravelTracker.slnx --configuration Release --no-restore`:
  passed, zero warnings and zero errors.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL"`:
  1,356 discovered, executed and passed; zero failed or skipped. This includes
  513 frontend cases, up by 15 from the baseline.
- The four JavaScript interaction suites used by CI: 41 passed, zero failed,
  skipped or cancelled, up by nine from the 32-case baseline.
- Whitespace and local documentation-link checks passed. Modified source has
  no Unicode replacement characters and no temporary Figma asset URLs.
- The frontend preview started on port 5100. HTTP checks returned non-empty
  successful responses for Home, Map, Trip, History, Sign-in, Not found, Error,
  the canonical stylesheet and all three new icons.

New behavior checks cover deferred single-click versus double-tap, header
dragging, hidden/inert content and draft preservation, mobile defaults,
cancellation/disposal, revealing collapsed feedback without a position reset,
Trip step transitions, attraction modal keyboard behavior during saves, and
feedback recovery actions. Existing authentication and service tests remain in
the application suite; test identities are not live-provider evidence.

The requested cloud browser could not open the local preview: its access host
reported that it was unavailable. A fresh browser screenshot comparison,
effective rendered asset geometry, physical-device checks and authenticated
live-provider journeys remain unverified. Figma screenshots establish the
design target; component and JavaScript checks establish behavior, not pixel
equivalence. An isolated PostgreSQL test connection is not configured locally.
