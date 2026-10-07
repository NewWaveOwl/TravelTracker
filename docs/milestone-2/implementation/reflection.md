# Reflection on the AI SDLC flow

Versioned guidance, issue/plan records and review evidence made the work inspectable. Separating reusable transport entries and locations from future scheduled journeys helped keep the scope understandable.

The AI initially carried forward too much search/PATCH/scheduling work and too many tests. Human feedback reduced that to three resources with POST/GET by ID and a small suite. Review exposed decimal Range rounding, and existing boundary tests verified the fix.

Human feedback also corrected the architecture twice: first require one Swagger host like LAB_07, then require that host to use collaborators' existing Api/Data/Domain projects. The separate WebApi preserved their files but duplicated models/interfaces. Moving controllers/services into Api, repositories into Data, and using the original Domain entities meets the intended integration. HTTP DTOs keep the agreed JSON aliases while preserving original model fields and interface signatures.

Source comparison confirmed no original files were deleted. The full build required an Id64 constructor conversion fix and declarations for four types referenced by unfinished interfaces. Empty partial declarations solve compilation only; their future behavior remains unimplemented and is explicitly documented. The full solution now builds with zero warnings/errors, the same 19 tests pass, and eight live checks verify the integrated API.

The later request to split tests by aim improved navigation without increasing coverage or complexity: one test project still runs 19 cases, now in four subject files with shared helpers. Review also caught stale one-file instructions in the AI guidance and documentation. Keeping those instructions aligned with the latest human decision prevents the agent from restoring an obsolete layout.

Earlier real Copilot CLI sessions produced plans, foundations and a review; Codex performed these corrections and validation. No new Copilot session or VS Code GUI Agent-mode evidence is claimed. Human judgment remained necessary to choose scope, notice duplicate architecture, accept limited coverage and review the PR before merge.
