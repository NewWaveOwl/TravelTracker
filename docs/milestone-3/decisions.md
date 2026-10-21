# Remaining contract decisions after M3-01

The repository owner explicitly directed on 2026-10-21: **treat #27 as complete and track remaining decisions separately**. #27 is therefore Done on the board, with [merged PR #61](https://github.com/NewWaveOwl/TravelTracker/pull/61) and its actual [Artiom](https://github.com/NewWaveOwl/TravelTracker/pull/61#pullrequestreview-5600941290) / [Christel](https://github.com/NewWaveOwl/TravelTracker/pull/61#pullrequestreview-5600949298) approvals as evidence. Do not reopen it or rewrite those reviews.

This register separates unresolved choices in [contract v0.1, section 9](https://github.com/NewWaveOwl/TravelTracker/blob/d719b400c3f2e4f3ca69614ad0dae8f878ff3ad4/docs/milestone-3-contracts.md) from completion of the initial contract work item. It adds no new implementation issues to the approved 24-item batch and makes no technical choices on behalf of the team. Existing issue assignees remain accountable for obtaining applicable decisions before implementing their slices.

| Decision | Required human input | Directly affected issues | State |
| --- | --- | --- | --- |
| D01 — frontend stack | Christel / team: React Native or an ASP.NET Core client; record the selected client and ownership. | #29, #32, #33 | Pending |
| D02 — OIDC provider | Artiom / Christel: choose the provider and document issuer/audience and subject mapping without secrets. | #33, #34, #37 | Pending |
| D03 — EF Core provider | Service owners: choose the development/provider verification target while retaining James's provider-neutral requirement. | #35, #36, #38, #41, #42 | Pending |
| D04 — trip/scheduled-route boundary | James / Artiom: confirm the minimum direct-leg versus scheduled-route boundary and location references, retaining the already recorded implied-departure invariant. | #39, #40, #41, #47 | Pending |
| D05 — Trips/Settings wire IDs | James / consumers: confirm the external identifier format and compatible mapping. | #40, #41, #42, #48 | Pending |
| D06 — catalogue ownership | Artiom / Christel: confirm private versus shared catalogue write permissions and access outcomes. | #35, #38, #43, #46 | Pending |
| D07 — scopes/status response | Artiom / service owners: confirm policy names and deactivated-user 401/403 behavior. | #34, #35, #36, #37, #38, #41, #42 | Pending |
| D08 — account standing | Christel: accept/refine/reject James's nullable `SpecialStatus` proposal and document compatibility. | #34, #37, #45 | Pending |

Record each resolution with the decision, human(s), date, contract revision/PR and approval link. Update affected issue descriptions and the board's dependency text when it is resolved. Do not infer a choice from a closed issue, selected library or generated code. No new GitHub issue batch has been created; if separate decision issues are desired later, present their titles/scope for human approval before publishing them.

The native blocked-by edges retain their historical work-item relationships. A closed #27 no longer blocks an item by itself. A still-applicable pending D01–D08 decision can keep the item Blocked; an item with no open issue/decision blocker returns to Backlog until its own Gate 1 is approved. In review/Done remain governed by the workflow's actual human and CI evidence.
