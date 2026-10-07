# Reflection on the AI SDLC flow

The versioned guidance, issue, planning notes and review evidence helped make the work reviewable. Separating reusable transport entries from locations was useful: dates belong to a future scheduled journey, while a route point can represent an accommodation, attraction or transport stop.

The implementation still grew too large. Earlier search/PATCH and scheduling requirements were carried forward after the human wanted a simple lab-style demonstration. Per-action partial files, custom validation and broad test matrices added more code than this scope needed. Human feedback corrected that: three resources with POST/GET-by-ID, 29 production C# files and 20 representative xUnit cases. The coding guidance now groups related resource methods as LAB_07 does.

Review also caught integer range attributes rounding decimal input; tightening existing boundary tests exposed and verified the correction without expanding the suite. Validation confirmed each new resource can be created and read through its returned URL, and both APIs run independently. It also kept the existing shared Id64 build error visible. Passing isolated service tests cannot establish full-solution success or future scheduling correctness.

This was a mixed tool flow: earlier fresh Copilot CLI sessions generated a plan, API foundations and a review; Codex implemented business code/tests and this scope correction. No new Copilot session or VS Code GUI Agent-mode evidence was produced for the simplification. Human judgment remained necessary to choose scope, reject unnecessary complexity, accept the smaller coverage tradeoff and review the PR before merge.


The human also corrected deployment: LAB_07 groups all current controllers in one application. The earlier SOA-oriented split required two processes and two test projects without helping this demonstration. Consolidating into one WebApi, one Swagger page and one xUnit project made inspection and execution simpler while preserving resource/layer boundaries. Only the duplicate health case was removed, giving 19 passing cases. This adjustment used Codex and retained the original shared scaffold.
