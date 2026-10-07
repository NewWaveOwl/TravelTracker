# Current implementation review record

Codex reviewed integration into the original Api/Data/Domain projects against the human's request, LAB_07 and both repository skills. [Validation](validation.md) records executed checks. The [historical Copilot review](copilot-review.md) applies to earlier code; no new Copilot or completed human PR review is claimed.

| Finding | Disposition |
| --- | --- |
| Separate WebApi duplicated existing entities/interfaces | Fixed: Api is the only web host, uses Data/Domain project references, and implements the original Attraction/TransportMode contracts. Added WebApi source is removed. |
| Concern about loss of collaborator code | Checked: all 26 original files remain; 20 unchanged and six reviewed integration updates. All 11 original interface files and every original model property/type remain. Main clone untouched. |
| HTTP fields differ from Domain fields | Fixed with response DTOs: DisplayName -> name, Latitude/Longitude -> lat/lon. The same 19 tests pass with unchanged bodies. |
| Existing constructor error blocked Domain build | Fixed: Id64 delegates using (long)New(), retaining its public constructors and conversions. Full build has zero warnings/errors. |
| Existing interfaces referenced undefined types | Compile-only resolution: empty partial Trip/TripLeg/UserSettings/TripMapPoint declarations. Their schemas/implementations/endpoints remain unfinished and must be defined by the owning feature. |
| Mutable Domain entities could expose stored instances | Fixed: Attraction/Transport repositories copy on read/write; RoutePoint is immutable. Singleton ConcurrentDictionary storage persists across requests. |
| Existing interfaces require more methods than current endpoints | Implement the complete preserved contracts internally; expose only POST and GET by ID. Internal update/collection/delete methods are not new HTTP routes and are not covered by the current endpoint suite. |
| Obsolete project folders still appeared in Explorer | Fixed: verify repository containment and generated-only contents, then remove old host/test folders. No unrelated source is removed. |
| Swagger/project/test layout | Checked: one Api host, one Swagger document with seven operations/six request-response schemas, one Api.Tests project/file, 19 passing cases, eight live checks. |
| Decimal validation | Retained decimal Range attributes and the existing boundary tests; invalid 5.1 returns 400 live. |
| Docs/skills described the standalone scaffold | Updated to the integrated architecture, current build result and planned trip-point/route relationships. Earlier evidence remains explicitly historical. |
| Search/PATCH/scheduling and workflow provenance | Deferred scope; issue #15 is related, not closed. This integration used Codex, without a new Copilot session. |

The suite covers the requested HTTP workflows and representative validation, rather than every internal contract method or possible input edge. Human review and merge remain pending.
