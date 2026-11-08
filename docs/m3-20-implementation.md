# M3-20 implementation evidence: accommodation search and selection

## Delivered

- Added a typed Accommodations HTTP client and public DTO/request in the Blazor frontend. Search supports blank browse and one encoded literal name filter; details and create use public accommodation GUIDs through `ProtectedClient`.
- Added a validated create form, private-by-default visibility, administrator-only shared option based on the live Users profile, and explicit loading, empty, read-only, validation, permission, unavailable and network states.
- Integrated the workspace into `/new-trip`. A server-confirmed record is retained in the in-memory trip draft; trip and stay persistence remain unconnected.
- Added session/sign-out handling and privacy safeguards that clear cached results and matching draft selection after `401`, account-wide `403`, or an inaccessible/removed detail `404`.
- Kept `catalogue.write` opt-in. Updated the README and Auth0 setup guide with the service URL, write-grant setup and draft-only limitation.

Design references: [search/results](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3364), [details/selection](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3488), [add modal](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=147-3619), [saved and ready to select](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3085), [unavailable](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-2836), [loading](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3198), [no results](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3304), [validation](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3419), [permission denied](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3566), and [connection failure/retry](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=143-3713). The in-app browser showed only part of the wide Figma canvas; a full-size visual comparison is not recorded as local validation evidence.

## Validation

- `dotnet restore TravelTracker.slnx --disable-parallel -m:1 -p:NuGetAudit=false` — succeeded using cached packages. A normal restore first failed with `NU1900` because `api.nuget.org` vulnerability data was unreachable. NuGet audit was disabled only for local restore; package versions were not changed.
- `dotnet restore TravelTracker.Tests.slnx --disable-parallel -m:1 -p:NuGetAudit=false` — succeeded.
- `dotnet build TravelTracker.slnx --configuration Release --no-restore -m:1` — succeeded, 0 warnings and 0 errors.
- `dotnet build TravelTracker.Tests.slnx --configuration Release --no-restore -m:1` — succeeded, 0 warnings and 0 errors.
- `dotnet test tests/TravelTracker.Frontend.Tests/TravelTracker.Frontend.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Accommodation" -m:1` — 26 passed, 0 failed, 0 skipped.
- `dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" -m:1` — 947 passed, 0 failed, 0 skipped. Data-test assemblies contain only PostgreSQL-category cases and therefore had no tests under this filter.
- `git diff --check` — passed.

The VSTest host needed loopback permission in this environment; the first restricted run could not start it. The successful test results above are from the subsequent loopback-enabled runs. No PostgreSQL test group was run because this change adds no persistence behavior. A live Auth0 tenant plus running Accommodations and Users services was not used, so the authenticated cloud end-to-end journey remains unverified.

## Review

The final read-only review reported **0 findings** and a **Ready to merge** verdict. Earlier review findings about stale cached catalogue data on `403`/`404`, the README Auth0 link and the plan approval note were resolved; the final review verified the access-denial cache-clearing behavior and its regression tests. See [review record](m3-20-review.md).
