# Azure demonstration deployment

## Architecture and scope

The selected target is **Azure for Students**, **Canada Central**, in a separate
`rg-traveltracker-demo` resource group. Supabase continues to host Auth and the
existing six PostgreSQL service databases. The Windows server continues to run
the authenticated routing/location backend behind its existing HTTPS address.
This deployment does not create another database, modify the Windows server,
enable Supabase's Data API, or run database migrations unless explicitly requested.

The Blazor frontend is a public HTTPS Container App. Users, Attractions,
Transport, Accommodations, Settings and Trips are separate Container Apps with
internal HTTPS ingress. Their authentication and authorization remain enabled.
The frontend and Trips service use those internal service addresses. Raw OTP
and geocoding-provider ports remain private behind the existing Windows API.

Each container uses 0.25 CPU and 0.5 GiB, with **at most one replica** and single
revision mode. The six APIs keep one replica warm by default; the frontend may
scale to zero and has sticky sessions. Keeping APIs warm consumes additional
Azure allowance/credit even between demonstrations. Use `-ApiMinReplicas 0` on
deployment or refresh to restore API scale-to-zero when accepting cold starts.
The environment must explicitly use `environmentMode: WorkloadProfiles`
(Standard Container Apps). Express does not support the certificate secret
volumes and frontend sticky sessions used here. This environment mode is separate
from the Standard container registry SKU. The deployment script checks the mode
before publishing images or prompting for credentials.
Current process-local sessions, ID allocation and temporary ticket imports do
not support arbitrary horizontal scaling. With API scale-to-zero selected,
cold starts can exceed a dependent service's timeout: retry after startup.
Restarts and new frontend revisions require users to sign in again.

## Files and costs

- [subscription.bicep](../deploy/azure/subscription.bicep) creates the isolated group.
- [infrastructure.bicep](../deploy/azure/infrastructure.bicep) creates a Consumption
  environment, Standard container registry and image-pull identity with AcrPull only.
- [apps.bicep](../deploy/azure/apps.bicep) configures the seven services.
- [Deploy-Demo.ps1](../deploy/azure/Deploy-Demo.ps1) publishes Linux images with the
  .NET SDK, then prompts privately for the existing service and database passwords.
  Docker Desktop and remote build tasks are not required.

The owner subsequently confirmed a free-service allowance of **31 Standard
Registry Unit days** and approximately 140 remaining Azure credit. Standard is
selected to match that allowance; Basic would not match the listed meter.
Coverage depends on the offer's remaining eligibility period and usage shared
across the subscription. After eligibility or allowance exhaustion, Standard
registry charges apply even when the apps are idle.
Container Apps has a free monthly allowance shared across the subscription;
excess compute, memory, requests and other applicable charges consume credit.
Open Blazor sessions can keep a replica active. This is a low-cost demo setup,
not a guarantee of zero cost or a spending cap. No paid Log Analytics workspace
is created. Inspect Azure Cost Management and remaining student credit.

Sources: [registry pricing](https://azure.microsoft.com/en-us/pricing/details/container-registry/),
[Container Apps billing](https://learn.microsoft.com/azure/container-apps/billing),
[.NET SDK container publishing](https://learn.microsoft.com/en-us/dotnet/core/containers/overview),
[Blazor hosting and affinity](https://learn.microsoft.com/azure/container-apps/dotnet-overview).

## Provision and deploy

Requirements: .NET 10 SDK, PowerShell 7.2+, Azure CLI and Bicep, plus a completed
local `.traveltracker-launch.json` setup with a valid public project key, runtime
database role and local Supabase root certificate. The existing service databases
must already have the required schema. Keep passwords out of source and chat.

```powershell
az login
az account set --subscription 'Azure for Students'
az bicep install
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.ContainerRegistry --wait
az provider register --namespace Microsoft.ManagedIdentity --wait

# Read-only Azure preflight. Inspect costs and predicted changes before creating resources.
az deployment sub what-if --location canadacentral `
  --name traveltracker-demo --template-file deploy/azure/subscription.bicep

# Creates resources; usage outside eligible allowances consumes credit.
az deployment sub create --location canadacentral `
  --name traveltracker-demo --template-file deploy/azure/subscription.bicep --output none

pwsh -File deploy/azure/Deploy-Demo.ps1 `
  -RoutingUrl 'https://YOUR-WINDOWS-ROUTING-BASE-ADDRESS'
```

The script prints the website address and confirms automatic backend service-token
renewal. Images use a unique timestamp tag; retain a known-good tag for rollback.
Secrets are held in Azure Container Apps secrets and referenced by environment
variable name. The root certificate is mounted at `/app/certs/supabase.crt`, and
Npgsql uses `SSL Mode=VerifyFull`. The temporary ARM parameter file is under an
owner-only directory in ignored `.artifacts/azure/` and removed in `finally`. A
crash can leave that private directory behind; remove that exact directory after
verifying its path. Do not publish it. Do not enable shell transcription/debug
tracing or Azure CLI `--debug` while entering or deploying credentials.

Before accepting the service token, the script checks its asymmetric signature
against the trusted project's JWKS, exact issuer, audience, expiry, subject,
role, non-anonymous identity and `users.access`-only scope, then verifies the
confirmed current user with Supabase. Privileged Supabase keys are refused.
The five dependent backend APIs receive `Supabase__ServiceEmail` and the secret
`Supabase__ServicePassword` (`service-password` secret) alongside the initial
`users-token` so `SupabaseServiceAccessTokenProvider` caches and automatically
refreshes their `users.access` bearer token five minutes before expiry. To rotate
credentials or replica settings without rebuilding images:

```powershell
pwsh -File deploy/azure/Deploy-Demo.ps1 -RefreshOnly `
  -RoutingUrl 'https://YOUR-WINDOWS-ROUTING-BASE-ADDRESS'
```

This retains the saved image tag, prompts again for credentials, and updates a
non-secret credential version in the five dependent API templates so they start
new revisions with the new secrets. Changing a Container Apps secret alone does
not update a running process. Expired or revoked service credentials still cause
protected operations to fail closed; do not extend JWT lifetimes or bypass Users
checks to conceal credential failures.

## Verification and operation

Before sharing the demo link:

1. Confirm all seven Container Apps are provisioned and ready. Verify that only
   the frontend has external ingress and that HTTP is redirected to HTTPS.
2. Open the frontend from outside the local network, sign in and read the profile.
   Verify signed-out access cannot read protected data. Confirm no HTTPS redirect
   loop and no mixed-content requests.
3. Upload a supported picture; independently sign in on another browser/device
   and retrieve it. Confirm another user's credentials cannot retrieve or replace
   it. Sign out and verify the device loses access.
4. Create/read a trip, retrieve catalogue data and save settings. A `/health` 200
   does not establish database access or valid service credentials. Exercise one
   request through every dependent API after cold starts.
5. Search an address and plan a route through the Windows API. Its HTTPS tunnel
   must remain running and use the same Supabase project. A stopped tunnel affects
   routing/location assistance, while manual planning remains available.
6. For registration and email-change demonstrations, configure Supabase Site URL
   and allowed redirect URLs to the new HTTPS frontend deliberately. The existing
   shared project's email templates use Site URL. Record the old setting and
   coordinate this change with teammates; existing-account sign-in does not need
   this change. Never disable email confirmation.
7. Watch credit and resource usage. Scale-to-zero does not stop registry charges.
   Stop using the demo when finished and remove only its isolated resource group
   when the owner requests cleanup; preserve Supabase and the Windows services.

The ingress is the only network entry to each container. The hosting configuration
enables ASP.NET Core forwarded-header handling so secure cookies and redirects
observe Azure's original HTTPS scheme. Do not expose raw container ports through
another route or reuse this proxy trust setting outside the controlled ingress.

Rollback is an incremental deployment of the same application template with the
previous known-good image tag and valid current secrets. It can interrupt local
sessions. Database migrations are disabled by default; a code rollback is not a
database rollback. Retain the local non-secret image-tag/expiry state in
`.artifacts/azure/demo-state.json` for token refresh.

## Evidence and remaining checks

### First deployment correction

The initial application deployment failed with `ExpressVolumeStorageTypeNotSupported`
and `ExpressStickySessionsNotSupported`. The original older-API environment
template omitted `environmentMode`; a current API query confirmed that Azure
had created an Express environment. These ARM validation errors do not indicate
bad passwords. The service identity already passed the script's authenticated
sign-in checks; PostgreSQL credentials had not yet been exercised.

The corrected template uses API `2026-07-01`, creates a new
`traveltracker-standard` environment and pins `environmentMode` to
`WorkloadProfiles`. The empty original environment is not converted in place.
All seven images from the first attempt can be reused, with a new service token:

```powershell
pwsh -NoProfile -File deploy/azure/Deploy-Demo.ps1 `
  -RoutingUrl 'https://YOUR-WINDOWS-ROUTING-BASE-ADDRESS' -ImageTag 'EXISTING-TIMESTAMP-TAG'
```

`-ImageTag` checks every image exists before asking for credentials. Do not use
`-RefreshOnly` until there is a successful deployment and local state file.
Neither certificate verification nor session affinity is removed as a workaround.
See [environment modes](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2026-07-01/managedenvironments)
and [Express limitations](https://learn.microsoft.com/en-us/azure/container-apps/express-overview).

On 2026-11-24, the replacement environment reached `Succeeded` and the current
Azure API returned `environmentMode: WorkloadProfiles`. Azure's resource-group
application preflight validation then succeeded using non-secret placeholder
parameters against the real environment, identity and uploaded image tag. This
confirms platform support for the requested deployment configuration; it does
not establish database access or complete live application verification. The
operator subsequently retried with fresh credentials, as recorded below.

### Successful live deployment

The operator retried with image tag `20261124001208` and fresh credentials on
2026-11-24. The application deployment succeeded in the Standard environment.
Website: [TravelTracker demo](https://traveltracker-frontend.politebush-89a65c90.canadacentral.azurecontainerapps.io).

Verified against the live deployment:

- All seven apps report `Succeeded` provisioning and `Running`, with a ready
  revision. The frontend alone has external ingress; six service APIs are internal.
- `/`, `/sign-in`, `/account` and `/trip` return 200 over HTTPS.
- Plain HTTP redirects with 301 to the same HTTPS website.
- Signed-out `/account` presents sign-in and has `Cache-Control: no-store,
  no-cache, max-age=0` and `Referrer-Policy: no-referrer`.
- All five browser layout scenarios pass against the live website: 320x640,
  390x844, 700x900, 844x390 and 1280x900, covering navigation, map controls and
  calendar sizing.

The operator subsequently verified trip saving and History, and authenticated
server checks verified address lookup and route previews, as recorded below.
Live picture persistence, independent-device sign-in and the browser routing
journey still require verification. Health and layout checks do not prove these
behaviors. The first service token expired at `2026-11-24T01:29:58Z` on that initial image.
A subsequent deployment (`20261124032208`) configured `SupabaseServiceAccessTokenProvider`
with the dedicated `users.access` service credentials in the five dependent APIs so
backend tokens renew automatically before expiry without manual `-RefreshOnly` runs.

### Email confirmation destination

After deployment, a signup email still pointed at `http://localhost:5100` because
the shared Supabase Auth Site URL had retained its development value. On
2026-11-24, the project dashboard's Authentication > URL Configuration > Site URL
was changed to the HTTPS frontend origin above, and the saved setting was
verified in the UI. The email template continues to use
`{{ .SiteURL }}/auth/confirm?token_hash={{ .TokenHash }}&type=email`; confirmation
and password policies remain enabled. New confirmation emails use the deployed
origin. Already-sent emails retain their old URL: replace only the localhost
origin with the deployed origin while preserving the path and query, or obtain
a new email if the one-time link has expired. Do not record real confirmation
hashes in documentation or logs. The shared project's default email destination
is now Azure, including registrations initiated during local development.

### API cold-start correction

On 2026-11-24 the deployed frontend logged eight-second `TaskCanceledException`
failures for trip history and catalogue reads used before saving a trip. Backend
startup timestamps aligned with these failures. Internal HTTPS connectivity
worked after startup; an authenticated Users lookup for a deliberately nonexistent
fixture ID returned the expected 404, exercising its database read without
exposing an account. The Npgsql missing-GSSAPI-library warning alone is harmless
and is not evidence of a database password failure.

The six backend APIs were changed to a minimum of one replica. The frontend was
not redeployed, preserving existing browser sessions. All six internal health
endpoints returned HTTP 200 from the frontend container after the change.
The deployment template and script now preserve this setting, with
`-ApiMinReplicas 0` available for lower idle consumption. The operator retried
History and Save in the existing browser session and confirmed both work after
the change. This is manual live verification, not an automated journey test.

Address lookup and route capabilities separately returned 404 from the configured
Windows tunnel. Keeping Azure APIs warm cannot repair those remote endpoints;
verify the current server Public HTTPS address and TravelTracker server update
before changing the frontend's routing setting. No authentication was bypassed.

### Windows routing repair over SSH

With operator authorization on 2026-11-24, connected through the existing
`traveltracker-server` SSH alias. The local Ktor port also returned 404 for
TravelTracker endpoints, ruling out an Azure-only connection problem. The
installed backend JAR differed from the verified Coverage update package.

Verified the update ZIP SHA-256
`21FA1C2FA10D47B195E585D16AC0FFE4B87825C80CF231C24B2A82ABE7E1C998`,
all payload hashes, and compatibility with the installed dependency filenames.
Stopped only the existing portable launcher's processes and its child tunnel,
ran the updater's CheckOnly and apply operations, then restarted through the
existing launcher start task. All 34 installed JAR hashes match the manifest.
The external settings hash remained unchanged; existing map data and Java/OTP
runtimes were retained. Backup on the server:
`D:\SERVER 2026\rnd_tranzit\RNDTransitServer-traveltracker-backup-20261123-200453-304-8a7f9e9f`.
Updater staging directory:
`D:\SERVER 2026\rnd_tranzit\TravelTracker-routing-fix-20261124`.

Live checks through the unchanged configured HTTPS tunnel passed:

- `/health` and `/health/traveltracker`: HTTP 200.
- Anonymous capabilities and reverse-address requests: HTTP 401.
- Authenticated capabilities: HTTP 200, pedestrian/bike/car/bus/train available.
- Authenticated reverse-address lookup at a public Montreal location: HTTP 200,
  success with one candidate.
- Authenticated car route preview between public Montreal coordinates: HTTP 200,
  success with one itinerary.

Protected smoke checks used the existing deployed service credential only in
memory, without recording it or retrieving private traveller data. These prove
the live server/provider path, not an independently signed-in browser journey.
No frontend redeployment or routing URL change was necessary.

Preparation on 2026-11-23: Bicep compilation passed; Azure subscription what-if
passed with five infrastructure resources to create. The application baseline
had 1,809 passed, zero failed and zero skipped non-PostgreSQL tests. The deployment
helper's 15 deterministic JWT/provider cases passed (including RSA and EC success,
signature/issuer/audience/lifetime/scope/identity failures). These checks do not
replace live container, database and cross-device verification. The subsequent
live deployment and repair results are recorded above.

The full Release build also passed with zero warnings and zero errors. A Linux
frontend image was published locally with the SDK and its archive manifest
verified: Linux/amd64, non-root `app` user, `dotnet /app/TravelTracker.Frontend.dll`
entry point and port 8080. The independent security review reported zero open
Critical or High findings after fixing secret refresh to roll dependent API
revisions. No hosting resources were created during this preparation; the
required Azure providers were registered. The owner then selected Azure and
authorized proceeding with the Standard registry and usage-based app hosting.
