# Critical journey test (#49)

`tests/TravelTracker.Journey.Tests` runs one traveller journey across the real Settings, Accommodations, Attractions, Transport and Trips hosts, in process. Each host keeps its production bearer handler, authorization policies, validation and PostgreSQL store, and gets its own disposable database. The hosts reach each other through an in-process router, so nothing touches the network or a production resource.

## What it proves

- A traveller loads defaults, saves settings with `If-Match`, and reads the saved value back after a reload; another traveller still sees the defaults.
- The traveller creates an accommodation, an attraction and a transport mode, plans a trip that references them (Trips validates each reference over HTTP and forwards the caller's token), and reads the trip back with its place snapshots; another traveller gets `404` for it.
- Every host refuses a missing token with `401`, and the Settings host also refuses a wrong-audience, expired or wrongly signed token with `401` and an under-scoped token with `403`. `/health` stays public on every host.
- Live account standing is enforced: a banned account can read its settings but not save them, and a deactivated account can do neither.

## What it does not prove

- Real Auth0. Tokens are signed by a local key that the hosts discover through the same issuer metadata path they use in production. The Auth0 tenant, grants and consent screen are not exercised.
- The real Users service. A small stand-in answers the internal identity and standing lookups. The Users service has its own tests.
- The Blazor UI. The journey runs at the HTTP contract level; the Frontend project's tests cover its pages and clients against fakes.

## Run it

Needs a PostgreSQL role with `CREATEDB`. Use a disposable cluster, never a shared or production one.

```
TRAVELTRACKER_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres" \
  dotnet test tests/TravelTracker.Journey.Tests -c Release
```

Set `JOURNEY_LOGS=1` to print each host's warnings and errors. The tests carry `Category=PostgreSQL`, so the CI PostgreSQL step runs them and the application step skips them.
