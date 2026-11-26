---
name: project-review
description: Review an assigned peer team's Milestone 4 project read-only and draft evidence-based work items for a TravelTracker teammate to confirm.
---

# Project Review

Follow the [course Milestone 4 Part 2 procedure](https://gabriellarco-jac.github.io/420-5W6-website/docs/project/milestone-4-part-2/). Use the [read-only agent](../../agents/project-review.agent.md) and the [TravelTracker assignments and record](../../../docs/milestone-4/peer-review.md).

1. Identify the assigned team, repository URL and integration branch. Prefer `feature/milestone-4-security` if present. Record the selected branch and full commit SHA so evidence remains reproducible.
2. Read startup instructions, architecture, threat model and milestone evidence first. The reviewer follows the documented startup steps and records the command, prerequisites and observed result or sanitized error. The agent can inspect supplied output. Leave both repositories untouched and keep peer code out of TravelTracker.
3. Inspect the implementation, tests and guidance at that revision.

   | Area | Evidence to inspect |
   | --- | --- |
   | Authentication | Validation of signature, issuer, audience and expiry. Trace identity to the authenticated principal. |
   | Authorization | Named policies or equivalent least-privilege checks, ownership rules, and denied access tests. |
   | Secrets and configuration | How credentials are supplied, environment boundaries and logging. Cite locations and redact values. |
   | Profile picture | File type and size checks, cross-device retrieval, owner-only access and replacement, and token handling in URLs, logs and profile data. |
   | Layering | Domain dependencies, controller responsibilities and the client's HTTP boundary. |
   | Tests | Automated success and failure coverage for each claimed security or profile-picture rule. |
   | Skills and agents | Guidance that contradicts implementation, omits a repeatable security check or grants writes beyond a role. |

4. Report each candidate with expected behavior, observed impact and a path and symbol, test, or supplied command result. A TravelTracker teammate checks each candidate personally and records the decision and reason. Record missing evidence as unverified. Exclude stylistic preferences.
5. Before filing, the teammate searches existing work items and records the search outcome. Add evidence to an existing open item when it covers the finding. Otherwise file a confirmed item with permission, or keep a ready-to-file draft. The owning team chooses the assignee. The agent only returns drafts.
6. Use P0 for blocking security issues or data loss, P1 for high-impact correctness or security, P2 for a defect or important missing test, and P3 for low-risk improvements. Include the milestone link, reviewing team, area, impact, sanitized evidence and observable acceptance criteria covering success and failure.
7. For each confirmed finding, identify any missing repeatable procedure in TravelTracker's skills or role constraint in its agents. Recommend one general change per gap, or explain why existing guidance suffices. The teammate records the decision and any later diff on the review branch.
