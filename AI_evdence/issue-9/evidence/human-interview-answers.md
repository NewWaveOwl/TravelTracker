# Human interview answers

Received in the working chat on 2026-10-06. The following requirements were supplied by the human, replacing the original provisional search-only scope.

**Feature: Attraction management and name search**

**Environment:** VS Code with GitHub Copilot, C# and ASP.NET Core on .NET 10.

**Starting point:** A standalone Attractions API on port **5104**, with seeded in-memory data.

Each attraction contains:

| Field | Meaning |
| --- | --- |
| `attractionId` | Generated GUID |
| `name` | Required attraction name |
| `description` | Optional text describing the attraction |
| `rating` | Optional manually entered score from 0 to 5 |
| `address` | Attraction address |
| `lat`, `lon` | Location coordinates |
| `estimatedVisitMinutes` | Typical visit duration |

**Endpoints**

| Method and route | Behavior |
| --- | --- |
| `GET /health` | Check that the service is running |
| `GET /api/v1/attractions` | List attractions |
| `GET /api/v1/attractions?name=museum` | Search by name |
| `GET /api/v1/attractions/{id}` | Get attraction details |
| `POST /api/v1/attractions` | Create an attraction |
| `PATCH /api/v1/attractions/{id}` | Update selected fields by ID |

**Create and update rules**

- Create returns `201 Created`, the saved attraction and its details URL.
- Update returns `200 OK` with the updated attraction.
- Names cannot be empty or whitespace-only.
- Ratings must be between 0 and 5; `null` means no rating is available.
- Invalid input returns `400`; an unknown attraction ID returns `404`.
- In-memory changes last until the service restarts.

**Search rules**

Search uses a trimmed, case-insensitive literal substring of `name`. Empty input returns all attractions; no matches returns `200` with `[]`. Results are ordered by name, then GUID.

**Verification**

Test health, list, details, search, creation and updates, including invalid inputs and unknown IDs.
