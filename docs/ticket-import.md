# PDF ticket drafts (#109)

Ticket import reads a PDF on the Trips server and proposes editable fields. It never creates or replaces a trip, changes its revision, matches stations to catalogue places, or infers missing information. Saving reviewed tickets and inserting itinerary entries belong to [#122](https://github.com/NewWaveOwl/TravelTracker/issues/122); pasted-text extraction belongs to [#110](https://github.com/NewWaveOwl/TravelTracker/issues/110).

## Design and entry point

The [Figma design](https://www.figma.com/design/7eOFtBm64Ep9fQXr0TtUSC/TravelTracker?node-id=92-3446) was inspected across the trip, map, history, catalogue, account, administration and feedback groups. The saved-trip design **02.5** has the intended **Tickets & bookings → Import tickets** entry. Import states **02.8–02.12** supply upload, extracting, editable review, completion and failure layouts. The existing shared navigation, fonts, buttons and feedback styles are reused.

The Blazor route is `/trips/{tripId}/ticket-import`. A saved trip supplies the owner boundary. Editing controls require `trips.write` and a live active Users profile read through `users.read`; a session that cannot establish standing sees a read-only notice. The backend upload policy itself requires only `trips.write` and performs its own live Users check. The completion state says **Ticket draft reviewed**, because this issue does not implement permanent ticket attachments. A failed import offers another PDF or blank manual review. Unsupported documents are discarded rather than attached. Existing itinerary data remains untouched throughout.

## HTTP contract

All routes are served by Trips on port 5108. Send the ordinary validated bearer token; never put a token into a document URL. The server checks the live Users standing and ownership of the saved trip before reading an upload or accessing temporary content. Administrators have no access to another traveller's trip or ticket. Unknown or inaccessible trip/import identifiers appear absent.

| Operation | Required scope | Result |
| --- | --- | --- |
| `POST /api/v1/trips/{tripId}/ticket-imports` | `trips.write` | A reviewable draft and private document location after successful extraction. Active owner only. |
| `GET /api/v1/trips/{tripId}/ticket-imports/{importId}/document` | `trips.read` | The owner's unexpired original PDF. Banned owners retain read access. |
| `DELETE /api/v1/trips/{tripId}/ticket-imports/{importId}` | `trips.write` | Removes the owner's temporary original. Active owner only. |

Upload a **raw `application/pdf` request body**, not multipart form data. The optional URI-encoded `X-Ticket-File-Name` header is display metadata only and never selects a storage path. Raw streaming and disabled form value providers avoid the framework's multipart temporary-file spooling. No endpoint accepts an owner, account status or role from the client.

The draft response contains `importId`, `tripId`, `fileName`, `expiresAt` and `fields`. Each field contains `name`, nullable `value` and `status` (`extracted`, `missing` or `uncertain`). Extracted values still require the traveller's review. Missing and conflicting information remains visible without fabricated replacements. The original is served through an authenticated Blazor endpoint for comparison, with restrictive document security headers and no caching.

| Outcome | HTTP status |
| --- | --- |
| Draft extracted / original downloaded / original cancelled | `201` / `200` / `204` |
| Empty upload | `400` |
| Missing or invalid token / missing scope or prohibited standing | `401` / `403` |
| Absent, inaccessible or expired trip/import | `404` |
| Upload read deadline / upload too large / wrong content type | `408` / `413` / `415` |
| Malformed, encrypted, unsupported or over-complex PDF | `422` |
| Parsing slots or temporary storage at capacity | `429` |
| Access dependency or trip storage unavailable | `503` |

## Supported input

- One PDF at a time, at most **10 MiB (10,485,760 bytes)** and **10 pages**. Empty uploads are rejected. Both the server and upload UI enforce the size limit.
- Text-bearing PDFs with explicitly labelled ticket fields are supported. Image-only scans need manual review; this version has no OCR.
- Supported field names are `ticketType`, `traveler`, `operator`, `bookingReference`, `from`, `to`, `departureDate`, `departureTime`, `arrivalDate`, `arrivalTime`, `timeZone` and `seat`. Extra document content is ignored, not returned as additional fields.
- Dates and times must be explicit and unambiguous. The parser does not infer a year, time zone, arrival date, location, operator or transport mode from unrelated document text.
- Malformed, encrypted and unsupported documents are refused with a safe error. PDF signature and structural validation precede extraction, and page limits are checked before ticket text is extracted.

The supported static subset uses standard PDF fonts (Helvetica, Courier, Times and their standard variants, Symbol or ZapfDingbats) and ordinary uncompressed or Flate streams without predictors. Embedded/custom fonts and maps, forms, image drawing, interactive actions, attachments, object streams and cross-reference streams require manual entry. A single canonical classic cross-reference table must match the final `startxref`; incremental and hybrid PDFs are unsupported. Limits include 2,000 objects and 10,000 indirect references. This intentionally supports a limited format rather than claiming to read every commercial ticket PDF. Local extraction uses [PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16).

Before page rendering, content is bounded to **100,000 decoded bytes, 10,000 candidate glyph bytes and 20,000 lexical tokens per page**, including each repeated content-stream occurrence. Object complexity is also bounded. Flate decoding allows at most **4 MiB per stream and 16 MiB across the document**. The reader also limits extracted text to **100,000 characters**, runs at most **two uploads/parses at once**, bounds body reads to **30 seconds**, and uses a **10-second cooperative parsing budget**. This is an in-process parser with explicit bounds, not an operating-system process sandbox or a guaranteed hard CPU deadline.

The current reader recognises the explicit English labels below, one label/value per text line. `Traveller` is also accepted for `Traveler`. Conflicting repeated labels are marked uncertain rather than selecting one value. Date values use `yyyy-MM-dd`, clock times use `HH:mm`, and the zone must be a recognised IANA identifier. For example, a synthetic text PDF can contain:

```text
Ticket type: Train
Operator: Example Rail
From: Example Station A
To: Example Station B
Departure date: 2026-11-23
Departure time: 08:35
Arrival date: 2026-11-23
Arrival time: 09:00
Time zone: America/Toronto
Booking reference: SAMPLE123
```

`Traveler` and `Seat` are optional labels. A different layout, language, ambiguous date or timezone abbreviation may require manual entry; unsupported formats are not interpreted as reliable ticket details.

## Private temporary storage

`TravelTracker.Trips.Data/TicketImports` owns a process-local, bounded memory store. It does not use PostgreSQL, Supabase Storage, a public folder, OS temporary files or an outside document service. Existing trip persistence remains PostgreSQL; no schema migration is required.

Every stored original is scoped to the exact validated **issuer + subject + trip identifier**, with a random import identifier. Reads return defensive copies. The lifetime is **15 minutes**, with access-time and timer-driven expiry cleanup. Cancellation, explicit deletion, expiry and host disposal release retained originals. Per-owner capacity is **5 documents** and service-wide stored content is capped at **100 MiB and 1,000 documents**. A restart discards all imports. A multi-instance deployment needs session affinity for these temporary originals or a separately reviewed private store.

Ticket bytes, extracted values, filenames and bearer tokens must stay out of application logs. Temporary originals are not browser-persistent drafts. Closing a review or losing a connection can leave an original until its bounded expiry; immediate remote deletion cannot be guaranteed after a network failure.

## Security boundaries and limitations

Assets are the original PDF, proposed personal travel details, and the owner's existing itinerary. Untrusted bytes cross the upload boundary only after authentication, live standing and owner checks. Size, page, format and parser limits reduce resource abuse; capacity limits bound retained content. Another owner's identifiers do not expose existence or content. A failed parse has no trip write path.

Parsing and temporary storage run on team-controlled systems. PDF extraction has no HTTP client or outside AI/OCR integration. The existing Users call establishes live access; it does not receive the uploaded document. Private PDF responses use attachment disposition, `no-store`, `nosniff` and a restrictive sandbox CSP. The app does not embed document content into its own page.

Process memory and authorised browser views still contain private data while in use. Runtime dumps, host compromise, stolen bearer tokens and authorised downloads are outside the protections of the temporary store. Keep host access restricted and use HTTPS outside loopback development. This feature does not claim permanent attachment storage or arbitrary-PDF/OCR support.

## Verification

The baseline on commit `c1ac652` was **1,072 application tests passed, 0 failed, 0 skipped** using:

```powershell
dotnet test TravelTracker.Tests.slnx --configuration Release --filter "Category!=PostgreSQL" --logger "trx;LogFilePrefix=issue109-baseline"
```

Final checks on 2026-11-22:

```powershell
dotnet restore TravelTracker.slnx
dotnet build TravelTracker.slnx --configuration Release --no-restore --verbosity quiet
dotnet test TravelTracker.Tests.slnx --configuration Release --no-build --no-restore --filter "Category!=PostgreSQL" --logger "trx;LogFilePrefix=issue109-final"
dotnet list src/TravelTracker.Trips.Api/TravelTracker.Trips.Api.csproj package --vulnerable --include-transitive
git diff --check
```

Restore passed. The final solution build reported **0 warnings and 0 errors**. The NuGet audit reported no known vulnerable packages, including transitive dependencies. All local Markdown links in the changed documentation resolved, and the diff whitespace check passed.

| Application test group | Baseline | Final passed | Failed / skipped |
| --- | --- | --- | --- |
| Trips API | 65 | 120 | 0 / 0 |
| Frontend | 377 | 410 | 0 / 0 |
| Temporary store (Trips.Data) | 0 | 19 | 0 / 0 |
| Other application tests | 630 | 630 | 0 / 0 |
| **Total discovered and executed** | **1,072** | **1,179** | **0 / 0** |

The 107 additional cases cover owner/issuer/trip isolation, scope and real bearer failures, live standing/outages, private PDF download/cancellation, supported extraction, missing/conflicting values, exact size/page boundaries, streamed oversize, encryption/malformed/active content, font/form/text amplification, concurrent parser/storage capacity, timed expiry, defensive copies, frontend review/manual recovery, late cancellation and session revocation. See [API tests](../tests/TravelTracker.Trips.Api.Tests/TicketImportTests.cs), [store tests](../tests/TravelTracker.Trips.Data.Tests/TicketImportStoreTests.cs), [frontend page tests](../tests/TravelTracker.Frontend.Tests/TicketImportPageTests.cs), [HTTP client tests](../tests/TravelTracker.Frontend.Tests/TicketImportClientTests.cs) and [private download proxy tests](../tests/TravelTracker.Frontend.Tests/TicketDocumentProxyTests.cs).

A separate loopback Kestrel smoke check used synthetic accounts, signed test JWTs and scripted Users/catalogues. A valid PDF returned `201` with 12 proposals, malformed content returned `422`, the owner downloaded the original with `200`, and another owner received `404`. The saved trip's body and `ETag` were identical before and after. This proves the real HTTP listener with isolated dependencies; it does not claim live Auth0 or database verification.

The PostgreSQL group was **not run**: `TRAVELTRACKER_TEST_POSTGRES` is not configured and no isolated PostgreSQL connection was provided. No database schema or trip persistence implementation changed. Browser visual verification was **not completed**: browser control was stopped with the physical Escape key. Component/HTTP tests passed, but no screenshot-match or responsive-browser pass is claimed.

The repository-required read-only security review finished with **0 unresolved Critical, High, Medium or Low findings** after the parser and access fixes. A different human teammate must still review the PR. The delivery branch is [`import-reviewable-pdf-tickets`](https://github.com/NewWaveOwl/TravelTracker/tree/import-reviewable-pdf-tickets); its Git history records the implementation commit referencing #109. PR creation remains a separate step. Generated TRX evidence uses the `issue109-baseline` and `issue109-final` prefixes under each project's ignored `TestResults/` directory. Temporary local smoke-check tooling remains in the ignored `artifacts/issue109/` directory.
