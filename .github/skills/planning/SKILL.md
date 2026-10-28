---
name: planning
description: Plan TravelTracker features and decide what to build next, grounding every decision in the docs/ contracts and the code that actually exists. Use for feature ideas, roadmap questions, scoping, and producing an implementation plan before issues or code; use issues to turn a plan into tasks.
---

# TravelTracker feature planning

The ground truth is `docs/`, then the code. Not memory, not the README's aspirations, not old plans.

Database decision 3 is resolved: the owner selected PostgreSQL with EF Core/Npgsql on 2026-10-28. Plan PostgreSQL migrations and isolated PostgreSQL persistence verification; do not reopen provider selection or propose SQLite as the project's storage. Keep the selected technology distinct from the current in-memory implementation, and preserve the remaining ownership/identity decisions as open until actually resolved. See [Data](../../../docs/data.md) and [contracts](../../../docs/milestone-3-contracts.md).

## 1. Establish ground truth

1. Read, in this order: `docs/design.md` (purpose, releases M1-M3, trip rules, capabilities), `docs/domain.md` (entities, identifiers, planned trip rules), `docs/api.md` (current contract), `docs/data.md`, `docs/trip-points-and-routes.md`, then `README.md` and `CONTRIBUTING.md`.
2. Read the source for the area you are planning. Build a two-column list: **implemented** (reachable through the running API and covered by tests) versus **planned only** (docs, empty partial types such as `Trip`, `TripLeg`, `UserSettings`, `TripMapPoint`, `schema.sql`, interfaces without endpoints).
3. When docs and code disagree, report the discrepancy. Do not silently pick one. Docs describe intent; code describes reality; the human decides which to change. Check `git log` and open issues/PRs so you do not plan work that is done or in flight.

## Lecture and milestone alignment

Use [skills-sources.md](../../../docs/skills-sources.md) to identify relevant course material. Map the requested milestone to observable acceptance evidence: independent services and contracts, event flow/recovery, security controls/threat model, layered tests, or controlled delivery. Do not infer milestone completion from folder names, placeholder models or resource controllers. Lecture examples and exercises do not authorize extra work; reconcile course requirements, product docs and explicit user scope, reporting conflicts. The old demo's host/resource restrictions are context rather than a permanent roadmap veto.

## 2. Come up with features

Derive candidates from the docs, never from imagination:

- Trace each capability in `docs/design.md` section 6 and each rule in section 5 (and `docs/domain.md` "Planned trip rules") to what is missing in code. Each gap is a candidate.
- Walk the traveller journey (plan a trip, view it on the map and itinerary, revisit history, edit/delete, settings) and note which step has no endpoint yet.
- Check the current-scope boundary in `.github/copilot-instructions.md`: only Attractions, Transport entries and Route Points exist; scheduling, search and PATCH need a separate request. Candidates outside it are labelled "needs scope decision", not planned silently.
- Respect "Not in scope (later)" in `docs/design.md` and the release a capability belongs to (M2 before M3; M2 must work with no outside service).
- Respect service ownership and privacy rules: private data stays private, each service owns its data.

For each candidate record: the doc section that justifies it, the user value, what it depends on, and its release.

## 3. Prioritise

Prefer, in order: items that unblock others (dependencies first, for example Trips before history); items inside the current milestone and scope; thin vertical slices that can be demonstrated through Swagger and tests; risk reduction. Park the rest. Give a recommendation, not a menu of everything.

## 4. Produce the plan

For the chosen feature write:

- **Goal and user value**, with the doc reference.
- **In scope / explicitly out of scope.**
- **Contract**: endpoints, request/response, status codes, validation rules, following `docs/api.md` conventions.
- **Design**: owning project and files per `code` (API/Data/Domain responsibilities, one primary type per file).
- **Acceptance cases**, including failure paths, in a form `testing` can turn into tests.
- **Docs to update** (`docs/api.md`, README tables, etc.).
- **Open questions** for the human, only those that change behavior or compatibility.
- **Risks and unverified assumptions.**

Keep the plan proportional to the feature. Then hand off to `issues` to break it into tasks. Do not start implementing from a planning request: planning stops at the plan, and you wait for approval.
