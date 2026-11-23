# Routing and address feature delivery

The owner requested #106, #107, #108, #113, #120 and #127 on 2026-11-22, with a
commit after each feature and Windows server update instructions. Existing
Public Nominatim integration is the foundation. The owner's explicit Search/Enter
choice overrides #113's original typing/autocomplete wording because the public
provider forbids autocomplete.

| Work | Acceptance and owning layer |
| --- | --- |
| Sign-in defect | Distinguish local sign-out from routing Auth configuration/rejection; optional routing failures preserve the app session. |
| #106 | Reproducible pinned OTP/Java/data setup, live mode/GTFS coverage evidence, additive capabilities and error contract; Kotlin integration. |
| #107 | Distinct walking/cycling requests and real geometry; selected endpoints/order; explicit acceptance and outage retention; Kotlin adapter and frontend HTTP contract. |
| #108 | Verified car capability or explicit unsupported; scheduled transit with stops/transfers, trip zone/date and coverage errors; Kotlin adapter. |
| #113 | Explicit address search, readable ambiguous candidates, keyboard selection, ghost marker, confirm/cancel without duplication; frontend. |
| #120 | Address/map entry in ordinary forms, small gray coordinates underneath, existing coordinates preserved, unknown-location fallback; frontend. |
| #127 | Origin/destination/date/time/mode selectors, available capabilities, alternatives, transit detail and explicit route confirmation/cancel; frontend. |

All integration stays outside pure Domain models. Preview calls do not save.
Provider attribution, public Nominatim's global one-request-per-second queue and
manual planning remain in effect. There are no schema changes in this scope.

Validation uses deterministic adapter/HTTP/UI success and failure cases, a full
.NET application suite, Kotlin backend/launcher suites and isolated updater checks.
Live OTP proof uses existing local Montreal OSM/GTFS assets and temporary private
ports; it does not alter the installed server or call Public Nominatim. Real
target-PC deployment, Dev Tunnel authentication and database/physical-device
checks must be reported separately from fixture verification.

Baseline: .NET Release build zero warnings/errors, 1,716 application tests passed;
Kotlin backend 73 and launcher 60 tests passed. The isolated PostgreSQL test
connection is not configured, so relational validation is unavailable; no
persistence change relies on an in-memory substitute.

Feature commits and final verification evidence are recorded below as delivered.

## Auth correction

Frontend `7fc6515` and shared backend `28a0da1` distinguish the app session from the
optional routing server's Auth configuration. The full .NET suite passed 1,719
tests; backend 75 and launcher 60 passed, with zero build warnings/errors. Regression
checks prove a Routing401 retains the session and later catalogue access, ordinary
API401 still revokes it, and anonymous address lookup sends no request.

## Address candidates (#113)

Explicit Search/Enter returns readable candidate addresses and an in-input spinner.
ArrowDown enters the result buttons; arrows/Home/End navigate them, normal button
activation selects a ghost marker, and Escape cancels and returns focus to the
input. The result count is announced. Repeated submit while a request is pending
does not restart it. Selecting and cancelling preserves existing coordinates and
stop count; applying marker details confirms one location. Provider attribution
stays visible. Desktop/mobile visual verification follows the completed form/UI
changes; it is separate from bUnit interaction tests.

## OTP mode and dataset proof (#106)

The [local verification record](otp-mode-verification.md) pins runtime, graph and
source hashes and records successful real WALK/BICYCLE/CAR/BUS/RAIL requests. The
shared backend adds protected capabilities from the current router schema and
loaded feed metadata, with unknown per-feed dates/bounds preserved. Its inclusive
date labels are paired with the exact exclusive service boundary; global union
dates never promise per-feed/per-day service. This is local route evidence, not a
claim that the Windows server or tunnel was updated or tested.

## Address-first ordinary forms (#120)

Attraction, Accommodation/QuickAdd, Transport-point and Trip forms now offer real
map selection and explicit address search. The selected coordinates remain visible
under the address in small gray text; numeric controls are optional and collapsed.
Embedded search cannot implicitly submit the containing save form. A missing
position has visible map-selection guidance. The picker keeps a private buffer,
and both candidate selection and using the location require confirmation. Cancel,
failed lookup and unknown address preserve existing form data and coordinates.
The compact trip editor can pick a map point, and applying an edited existing
marker updates that marker without adding a duplicate.

Validation: 680 frontend tests and 1,734 tests across the ordinary application
suite passed, with zero failures/skips and zero Release build warnings/errors.
The twelve added cases cover unknown positions, map/search confirmation, cancel,
failed reverse lookup retaining manual text, embedded form isolation and compact
trip-map entry. Read-only source review found no remaining actionable issues.

## Walking/cycling previews (#107)

The shared adapter preserves chosen endpoint order, requests actual WALK or
BICYCLE paths and returns the provider's geometry, distance and duration. Its
optional IANA zone validates the supplied offset against that date's daylight
saving rules; absent/null zone remains compatible with old requests. The response
keeps the requested departure and zone. Geometry point counts, connected times and
duration spans are validated, including waiting time rather than only movement.
No-route, malformed provider data, timeout and outage have separate recoverable
outcomes. Confirmation remains an explicit frontend action in #127.

Validation: the complete Kotlin gate passed 111 backend and 60 launcher tests
without compiler warnings. The 23 new adapter/HTTP cases cover zone compatibility,
DST ambiguity/gaps, endpoint and geometry consistency, realtime delays, no-route,
timeout/cancellation cleanup and sanitized outages. Actual five-mode raw responses
also passed the new declared-point-count and scheduled timing invariants.

## Planner selection and confirmation (#127)

Map tools → Transport offers explicit capabilities/catalogue loading, selected
endpoints, date/time in the trip zone and supported modes. Windows zone IDs map to
IANA without changing the saved trip. Alternatives display actual times, initial
waiting and available stop/transfer details. Only explicit Confirm applies the
saved transport and exact seconds to an adjacent connection. Preview/Cancel does
not write the itinerary or reorder stops. Confirmation rechecks endpoint/revision,
unconfirmed markers and the exact saved-mode ID, updates the parent immediately,
and participates in Undo. Geometry stays within the circuit/session, is cleared
for a different account/trip and is not a new persistence field.

Confirmed provider geometry replaces the matching leg's approximate connection,
and its edit control follows the midpoint of the generated path. Other legs
keep their existing connections. Return visits are matched by stop occurrence;
missing or malformed geometry falls back to the approximate connection. Undo
or a changed route context restores that connection. Component regressions cover
replacement, edit selection, return visits, malformed geometry and Save/Undo;
the latest replacement has not received a fresh rendered browser check.

Validation: 702 frontend tests and 1,756 ordinary application tests passed, with
zero failures/skips and zero build warnings/errors. The 22 added cases include
Windows/no-slash IANA zone compatibility, DST choices, options gating, stale and
nonadjacent selection, malformed legacy geometry, wrong catalogue ID and the full
Cancel → Confirm → Undo → Confirm → Save path. Read-only review found no remaining
issues. Responsive browser verification is recorded separately after inspection.

## Car and scheduled public transport (#108)

The adapter uses verified actual CAR/BUS/RAIL modes and exposes explicit unsupported
mode/dataset outcomes. The TravelTracker-specific query adds provider positions,
boarding/alighting IDs, intermediate stops, interline/transfer data and headsigns
without changing Android's query. Effective and scheduled times remain distinct.
Itinerary duration spans actual first departure to final arrival; initial wait
and elapsed time from the requested departure are separate fields. Coverage can
be unknown when optional metadata fails. Actual router errors, rather than a
global calendar union, decide missing-date/location/stop/no-route reasons.

The dedicated query was exercised against the real isolated Montreal graph:
all five modes succeeded with unchanged metrics, named transit endpoints and
provider coordinates; bus alternatives reported 15–17 intermediate stops. The
sampled rail route had no intermediate stops. Those live samples had zero
transfers; deterministic multi-leg fixtures cover transfer/interline behavior.
Raw evidence is in RNDbackend's ignored
`backend/build/routing-evidence/M4-108-root-tt-query-20261122/` directory.

Validation: 132 backend and 60 launcher tests passed without compiler warnings,
failures/errors or skips. The 21 new cases cover car fallback, missing rail data,
service dates, multi-vehicle/interline transfers, stop metadata, incomplete
realtime, no-route and outages. The opt-in offline replay normalized all 11 real
alternatives across the five modes through the current production adapter.

## Responsive browser verification and follow-up

An isolated Edge fixture rendered the actual Blazor components at desktop
1440×1000 and phone 390×844 with touch/reduced-motion emulation. Its authentication,
address, catalogue and routing HTTP responses were simulated; the normal public
basemap tiles loaded. This verifies browser interaction/layout, not live provider
authentication or a physical device.

All 31 UI assertions passed with zero application page errors. Normal keyboard
and pointer interactions verified explicit search/no typing requests, in-input
spinner, reduced-motion behavior, candidate focus/Escape, manual/cancel retention,
ghost selection, Apply once, Undo, transit detail, route Cancel/Confirm and an
explicit Save preserving `10:00:05`/`10:07:37` seconds. Both viewports fit
horizontally. No forced or evaluated clicks were used.

The phone run found a real overlay defect: Save/status controls intercepted
Transport and covered address input. A narrow-screen-only `NewTrip.razor.css`
follow-up puts that container in normal flow above the movable panels. The same
real click sequence then reached Map tools → Transport → Train; desktop styling
is unchanged. The affected frontend still passed all 702 tests and built with
zero warnings/errors. The fixture host and owned browser processes were stopped.

Local reports/screenshots remain in the ignored
`tests/TravelTracker.Frontend.Tests/TestResults/browser-qa/` directory, including
`report.md`, `report.json`, `desktop-address-spinner.png`, `phone-tools.png`,
`phone-address-spinner.png`, `phone-route-confirmed.png` and `phone-saved.png`.
The primary agent also inspected representative desktop and phone screenshots.

## Feature commits

| Issue | Repository | Feature commit |
| --- | --- | --- |
| #106 | RNDbackend | `e69cc47` |
| #107 | RNDbackend | `99f48e0` |
| #108 | RNDbackend | `58d68ff` |
| #113 | TravelTracker | `e4a1b60` |
| #120 | TravelTracker | `8ce3841` |
| #127 | TravelTracker | `399732b` |

The sign-in correction is `7fc6515` in TravelTracker and `28a0da1` in RNDbackend.
The mobile layout correction is `ea071f2`; delivery evidence is a separate follow-up.
At the end of the initial 2026-11-22 delivery, commits were local and the earlier
empty backend branch was the only published work. The prior Auth0 README edit remains preserved in its named
stash. See [the verified ZIP and exact server/app commands](server-routing-update.md).

Final post-CSS gate: Release solution build zero warnings/errors; all 1,756
ordinary application tests passed again with zero failures/skips. PostgreSQL's
marked group was not run because no isolated connection was configured; these
features have no schema or persistence edits. The real server/tunnel, public
geocoder response quality, physical devices and live realtime feeds were not
validated by the local fixture/artifact checks. Source worktrees are committed
locally at that point; no push, PR or actual server update was part of that initial delivery.

## Recovery fixes and publication verification (2026-11-23)

Frontend commits `5e0db39` and `cd27913` add early image-dimension rejection and
specific, sanitized routing setup/connection guidance. `e1d3a03` records the
confirmed missing `UserPictures`/`TripStops` tables and existing migration command,
plus the current server's `authentication_unavailable` result. These are separate
operator repairs; see [startup error recovery](startup-error-recovery.md).

The current Release build passed with zero warnings/errors. All 1,781 application
tests passed (727 frontend), plus all 55 Node keyboard/panel interaction cases.
Rebuilt actual-component browser fixtures passed 41 checks at desktop and phone
viewports with zero page errors: the prior 31 address/transit checks plus 10 Bike
checks for preview, Cancel, Confirm, visible Undo and exact-second Save payloads.
These use synthetic Auth/catalogue/provider responses. Phone testing emulates a
390-by-844 viewport, not a physical device. Ignored evidence is under
`tests/TravelTracker.Frontend.Tests/TestResults/browser-qa-final/`.

The backend was freshly rebuilt/retested: 132 backend and 60 launcher cases passed
with zero failures/errors/skips/warnings, and production-adapter replay passed all
five modes and 11 alternatives from retained actual OTP responses. A new full
graph build and live realtime feeds were not tested. Local marked PostgreSQL
tests remain unavailable without the isolated test connection; GitHub CI provisions
its own disposable PostgreSQL for that group.

The [separate backend branch](https://github.com/NewWaveOwl/RNDTransitMTL/tree/backedn-for-TravelTracker)
is now pushed at `080ae05`, including the tested implementation and diagnostic AI
record. Its last runtime change is still `58d68ff`; later commits change docs only,
so the verified Windows update ZIP remains current. Backend source is delivered
on that branch rather than merged into RNDTransit's `main` by the TravelTracker PR.

The operator reports the current update/tunnel are deployed. Read-only probes
confirmed public health and the Auth setup failure; applying the database
migrations, fixing the server's TravelTracker Auth pair, and successful live
account/address/route/save journeys after repair have not been observed.
