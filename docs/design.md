# Design

## 1. Purpose and scope

TravelTracker puts a traveller's journeys, accommodation stays and attraction visits into one itinerary. Travellers plan a trip, see it on a map next to a day-by-day list, and revisit past trips in a history.

This document says what the product does and how it is organized. It leaves technology choices to implementation.

The release labels below describe the product roadmap. The current runnable demonstration implements only Attractions, Transport entries and Route Points; the remaining capabilities are planned.

**Goals**

- Plan a complete trip by hand, with no outside service required.
- Keep the map and the itinerary list in sync.
- Keep every traveller's data private.
- Let each backend service run and be demonstrated on its own.

**Not in scope (later):** live tracking, real-time transit, booking or payment, shared trips, offline maps, airports and stations as trip endpoints, commercial ratings.

**Releases**

| Release | What the traveller gets |
| --- | --- |
| M1 Foundation | Six services running independently with sample data |
| M2 Core app | Accounts, places, trip planning, map, history, editing, deletion, settings, admin tools |
| M3 Planning help | Free-time summary, nearby suggestions, ticket import with review, timeline editing |

M2 must work fully without any outside service. M3 extras never block M1 or M2.

## 2. System overview

The planned backend has six logical services. Each is intended to own its data and communicate through public interfaces. Currently, the runnable demonstration uses the existing Api/Data/Domain projects to host Attractions, Transport entries and Route Points together. Collaborators' original models and interface contracts are retained. The table records ownership and planned responsibilities; it does not imply all six services run today.

| Service | Owner | Responsibility |
| --- | --- | --- |
| Users | Christel | Sign-in, profiles, roles, account status, admin audit |
| Accommodations | Christel | Lodging places |
| Transport | Artiom | Transport entries: car, bus, train, pedestrian, bike, plane; typed route points; scheduled journeys are future work |
| Attractions | Artiom | Attraction places; later opening hours |
| Trips | James | Itineraries, history, map data, time calculations; later ticket import |
| Settings | James | User preferences |

**Saving a trip.** The whole itinerary is saved as one unit. The app checks the user, confirms every place and transport mode used, applies the trip rules, and keeps its own copy of each place's name and location. If anything fails, nothing is saved.

**When something is down.** The user gets a clear message and the form keeps what they typed. Saved trips stay readable even if the place catalogues are unavailable. Sign-in is always checked live, so if Users is down, protected actions wait rather than trusting stale permissions.

## 3. Accounts and access

Every account is a traveller or an administrator, and is active, banned or deactivated.

| Who | Can do |
| --- | --- |
| Visitor | Register and sign in |
| Active traveller | Manage own profile, settings, trips and private places; use the shared catalogue |
| Banned traveller | View own data and sign out; change nothing |
| Deactivated or deleted | Nothing |
| Administrator | Manage accounts and the shared catalogue; cannot see other people's private trips |

- **Privacy.** Another person's private item looks like it does not exist. Something you can see but not change is refused as forbidden.
- **Sessions.** Signing in starts a private session that ends automatically after a period of inactivity, and also on logout, password change or deactivation.
- **Admin safeguards.** Every status change needs a written reason and leaves a permanent audit entry. The last active administrator cannot be disabled or deleted.
- **Deletion.** Deleted accounts and trips vanish immediately. Their data is cleaned up in each service afterwards, with progress visible to administrators, and removed for good after a grace period.

## 4. Main concepts

| Concept | Meaning |
| --- | --- |
| Account | A person, with role, status and contact details |
| Accommodation | A place to stay: name, location, shared or private |
| Attraction | A place to visit: like an accommodation, plus a typical visit length and category |
| Transport mode | How a journey is made |
| Transport entry | A named catalogue entry with a supported mode code and optional description |
| Route point | A reusable accommodation, attraction or transport-stop location snapshot |
| Trip point | One ordered occurrence of a route point in a trip; the same location may be used more than once |
| Scheduled transport route | One transport entry travelling between route points at supplied departure/arrival times, with preparation and optional intermediate stop timings |
| Trip | A named, dated journey, one way or round trip, shown in a chosen time zone |
| Leg | One ordered trip journey selecting a scheduled transport route between trip-point occurrences |
| Stay | A booking at an accommodation, with check-in and check-out |
| Visit | Time spent at an attraction |
| Time block (M3) | A period the traveller marks as rest or unavailable |
| Settings | Theme, default map view, preferred transport, time zone; later interests and available hours |

- **Places** are private to their creator unless an administrator adds them to the shared catalogue. Travellers can add their own places while planning. A place that should no longer be chosen is archived, never deleted, so old trips keep working.
- **Saved copies.** A trip keeps its own copy of each place's name and location. Editing the catalogue never moves an old trip's markers.
- **Trip status** (upcoming, current, past) is worked out from the trip's dates when viewed. It reflects the plan, not proof the traveller went.
- **Concurrent edits.** If two people or two tabs change the same record, the later save is refused and the user can reload or review their changes rather than overwriting.

**Local service demonstration.** The current TravelTracker.Api hosts POST and GET by ID for Attractions, Transport entries and Route Points together on port 5104, with one Swagger page, seeded process-lifetime memory and GUIDs. The independent-service roadmap remains future work. Route-point source IDs are optional unverified metadata. The scheduled-route concept above and the trip rules below describe future product design; schedules, boarding/dwell calculations and attraction suggestions are not implemented in the reduced demonstration. See the [current specification](https://github.com/NewWaveOwl/TravelTracker/blob/milestone_2_AI_and_development_resource/devlopmet_flow/atiom_developmet_flow/implementation/specification.md).

**Trip-point and route relationship.** A route point stores a reusable location. A trip point represents one use of that location in an itinerary. A transport entry stores the mode, name and description; a future scheduled route adds the start/end points, departure/arrival, preparation and ordered intermediate stops. Trip legs select those routes and retain snapshots. Station/airport points can be route endpoints or intermediate stops while the whole trip still follows the accommodation/attraction rules below. The [relationship diagram and timing design](trip-points-and-routes.md) explain this planned integration.

## 5. Trip rules

The app only saves a trip that makes sense as a real journey. In plain terms:

- **A trip is a connected chain.** Every leg starts where the previous one ended, so the traveller never "teleports" between places.
- **One way** trips go from a place the traveller stays to an attraction. **Round trips** return to where they started and include at least one attraction.
- **Time flows forward.** Journeys happen in order, each takes some time, and the traveller cannot be on two journeys at once.
- **Visits fit the journey.** A visit happens at a place the traveller has actually reached, before they leave it, and never at the same time as another visit or a journey.
- **Stays are bookings.** A hotel booking can span the time the traveller is out travelling or sightseeing, so it never conflicts with them.
- **Everything stays inside the trip's dates.** The app may offer to extend the trip but never changes the dates on its own.
- **Places keep their meaning.** The app never silently reconnects, reorders or corrects the user's choices; it points out the problem and lets the user fix it.
- **Times are local to the trip.** Users enter times in the trip's time zone, and the app asks for clarification when a local time is ambiguous or impossible.

Every problem is reported next to the field that caused it.

**Free time (M3).** Free time is the part of the traveller's available hours (adjustable per trip, with a sensible daily default) that is not taken up by travel, visits or time blocks the traveller has marked. A hotel booking does not count as busy, because otherwise nothing could be suggested while the traveller is at their hotel. The trip summary shows total free time and the individual free windows, explains which hours were counted, and shows "not enough information" for drafts that lack times.

**Nearby suggestions (M3).** From a free window the traveller picks a start place, transport mode and interests. The app lists attractions that fit: travel out, the visit, travel onward and a small buffer must all fit inside the window. Each suggestion explains why it fits. Attractions with unknown opening hours appear separately and are never labelled "open". Travel times are marked as estimates. Nothing is added automatically; the traveller reviews and saves through the normal editor.

## 6. Capabilities

What each part of the system lets people create, read, update and delete.

| Resource | Create | Read | Update | Delete | Who |
| --- | --- | --- | --- | --- | --- |
| Accounts | Register | Profile; admin search | Profile, password, status (admin) | Admin, audited | Self or admin |
| Session | Sign in | Current session |     | Sign out | Everyone |
| Accommodations | Add place | Search, view | Edit, archive | Archive only | Travellers (private), admin (shared) |
| Attractions | Add place | Search, view | Edit, archive | Archive only | Same as accommodations |
| Transport modes | Preloaded | List, view | Preloaded | None | Signed in |
| Settings | First save | View | Replace | None | Self |
| Trips | Create whole trip | List with filters, view | Rename, edit notes, replace itinerary | Delete | Owner |
| Map data |     | Points and route lines for a trip |     |     | Owner |
| Summary (M3) |     | Days, elapsed time, free time |     |     | Owner |
| Suggestions (M3) |     | Attractions that fit a window (changes nothing) |     |     | Owner |
| Ticket imports (M3) | Upload | Status and proposed fields | Confirm reviewed fields | Cancel | Owner |

## 7. User interface

**Navigation.** Map, New Trip, History and User. Settings sits under User, and Admin appears only for administrators. Direct links obey the same permissions as menus.

**Map screen.** A large map with the itinerary list beside it. Events are grouped by day with times, place, type and transport. Choosing a list item highlights its marker and vice versa. Markers are numbered with a legend, and each segment shows the transport icon, mode and duration. Straight lines are labelled approximate and never suggest a real road or flight path. There are zoom, fit-trip and legend controls plus a text version of the itinerary. On phones, Map and Itinerary become tabs sharing one selection. If the map cannot load, the list and details keep working.

**Trip editor.** Four steps: basics, itinerary, stays and visits, review.

- A round trip shows an explicit return leg; the app may copy places but never invents times.
- Place search shows shared and private places, with Add Place when nothing matches.
- Legs can be added, removed and reordered (with a keyboard alternative to dragging). A broken connection is impossible by construction, because each leg's departure is the previous leg's destination. Reordering shows the resulting chain and highlights time clashes but never fixes them silently.
- Errors appear next to each field and in a summary that jumps to the first problem.
- Saving works only when the form is valid, cannot be double-submitted, and moves on only after the save is confirmed.
- If saving fails, nothing typed is lost. Unsaved work is kept only in memory, so leaving the editor asks first.

**Other screens**

- **Trip details:** status, dates, legs, stays, visits and notes, with Edit, View on Map and Delete (with confirmation). A current-trip panel highlights the active or next planned event, based on the plan, not location.
- **History:** past trips first, newest on top, with search, date filter and paging. Later, several trips can be shown together on the map.
- **User and Settings:** profile and password are separate forms; settings cover theme, default map view, preferred transport and time zone.
- **Admin:** search accounts, change status with a reason, and see audit and cleanup progress. Nothing shows success until it is confirmed.
- Banned users see a read-only notice and no editing controls.

**Look and feel.** Earthy greens and sand for the main surfaces, blue for route lines, coral for emphasis and warm yellow for attractions, following the supplied concept. Text is easy to read at any size, controls are comfortable to tap, and keyboard focus is always visible. The design aims for WCAG 2.2 AA accessibility, works in light and dark themes, never relies on color alone, and offers a text version of everything on the map. Every screen handles loading, empty, error, permission and out-of-date states.

## 8. Ticket import (M3)

Travellers can upload a ticket or booking (PDF or image) to pre-fill a draft: Upload, Validate, Extract, Review, Confirm, then save through the normal editor.

- Extraction only proposes values. The traveller sees the original beside the extracted fields, with missing or uncertain ones flagged, and confirms before anything reaches an itinerary.
- Unknown fields stay empty; nothing is guessed.
- Airports and stations are only ticket details in this version. The traveller picks the matching accommodation or attraction.
- A failed extraction never touches an existing trip, and manual review is always possible.
- Files are private to their owner and can be attached to a leg, stay or visit. Extraction runs entirely on the team's own systems; tickets never go to outside services.

## 9. Outside data and services

Everything the product relies on must be free for its intended use: no trials, no payment details, no surprise charges. If a free quota runs out, the feature turns off or falls back to local data. Manual entry never depends on an outside lookup.

| Need | Approach | If unavailable |
| --- | --- | --- |
| Cities and countries | Open dataset, searched locally | Type the location |
| Airport codes | Open dataset, searched locally | Choose or enter manually |
| Hotels and attractions | Regional open map data imported into the catalogues; optional public lookup, off by default | Search local data or drop a pin |
| Base map | Open map tiles with visible credit | List and details still work |
| Route estimates | Optional self-hosted routing | Use entered times; show "estimate unavailable" |
| Ticket reading | Text and image recognition done locally | Manual review |
| Ratings | None | "No rating available" |

Principles: outside results are candidates the user confirms, never automatic saves; a city center is never used as a hotel position; a straight-line distance is never presented as a journey time; missing data shows as unknown, not guessed. Commercial rating services are excluded because they need billing, and reviews are never copied or scraped.
