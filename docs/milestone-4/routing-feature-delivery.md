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

Validation: 702 frontend tests and 1,756 ordinary application tests passed, with
zero failures/skips and zero build warnings/errors. The 22 added cases include
Windows/no-slash IANA zone compatibility, DST choices, options gating, stale and
nonadjacent selection, malformed legacy geometry, wrong catalogue ID and the full
Cancel → Confirm → Undo → Confirm → Save path. Read-only review found no remaining
issues. Responsive browser verification is recorded separately after inspection.
