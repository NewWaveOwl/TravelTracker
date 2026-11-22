# Figma alignment follow-up — PR #133

This is a historical comparison. The updated 2026-11-22 canonical design and
implementation are recorded in [Canonical UI alignment](canonical-ui-alignment.md).

Source: [TravelTracker — FianlView](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3446).

Used the `figma-design-to-code` skill and official Figma tools. The supplied node is a whole canvas; its initial design-context call required a layer selection. Metadata identified the relevant child frames, then high-fidelity design context and screenshots were retrieved for each comparison below before editing. The Figma document was not modified.

| Reference frame | Comparison |
| --- | --- |
| [181:2898 — Trip basics](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=181-2898) | Forest itinerary surface, sand tools/editor surfaces, rounded panels, typography and map-overlay layout. |
| [181:3084 — Plan stops and routes](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=181-3084) | Compact map tools, selection colors, button hierarchy and original icon slots. |
| [181:3471 — Review on map](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=181-3471) | Full-width primary Save trip action in the right review panel. |
| [92:3687 — Sign In reference](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3687), [160:2933 — First administrator setup](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=160-2933) | Existing Aldrich primary buttons and Atkinson Hyperlegible text reused by the added failure/retry feedback. No new auth/admin page theme introduced. |

## Corrections to this PR's additions

- Replaced the large white, bordered movement bars with compact Move/Reset utilities on their own panel's surface. The 44-pixel targets, full accessible labels, drag handles, arrow/Home behavior and child state remain intact.
- Applied the design's forest `#3a4b37`, sand `#ece2ca`, ink `#293428`, selection peach `#ffccbd`, and 12-pixel panel corners through scoped shared properties. Continued using the existing local Aldrich and Atkinson Hyperlegible fonts.
- Replaced the full-width cream save strip with a compact sand action group in the central map-overlay area on large desktops. The desktop group is fixed below the 120-pixel header so opening saved-trip browsing and scrolling cannot hide Save. At 1600 pixels and below it returns to sticky document flow, preventing wrapping controls from overlapping Map tools. The initial desktop placement, narrowed desktop layout and 400×716 device emulation were checked visually; the desktop scroll-persistence correction preserves that initial placement.
- Restored the design's full-width, 60-pixel primary Save trip action in the right review panel. It and the prominent workspace action share `SaveWorkspaceAsync`; the existing duplicate-submission test now exercises both buttons while a save is pending. No save validation or catalogue-confirmation rule was weakened.
- Used inherited ink/light focus outlines rather than low-contrast orange on sand. Calculated contrast is 10.08:1 for ink on sand and 9.21:1 for light text on forest.

## Assets and verification

Reused existing local assets rather than redrawing or substituting Figma icons. Inspected non-empty file metadata and root dimensions: logo 94×94, account 64×60, menu 69.3335×48, transport/stop glyphs 24×24, map-center 19×19 and zoom glyphs 12×12 / 12×2. Checked their existing header, map-tool and map-control callsites and visible geometry in the running preview. No temporary Figma asset URL was added to application code, and the reference screenshot is not used as a runtime background.

- Release solution build: passed, zero warnings/errors.
- Frontend suite: 470 passed, zero failed/skipped.
- JavaScript interaction suite: 21 passed, zero failed/skipped.
- Native Opera: desktop map/edit and review states; narrowed desktop layout without toolbar overlap; 400×716 emulation with visible Save/Reset, reachable panels and working drag/Home reset. Browser console showed the existing three external map-style filter warnings; no new application exception in the fresh preview.
- Read-only review identified the orange-on-sand focus contrast issue, which was corrected before delivery.

## Scope of the alignment claim

The added controls now follow the referenced design's visual language and restore its review save placement. The design has no drag-handle/global-reset or always-visible workspace-save variant; these required behaviors are styled extensions, not a claim of a pixel-identical existing Figma frame.

Pre-existing differences remain: later #117/#123 requirements changed Basics/Stops and Map/Trip navigation, trip basics already live in the itinerary panel, and the running map displays user-selected locations rather than the mockup's Moravia sample itinerary. Existing auth-page layout differences were not rebuilt. No blocked address-search, ticket-import or routing feature was presented as complete through a visual mockup. Physical-device and live authenticated-provider visual verification remain unavailable.
