# Iteration Log

## 2026-04-24 — Replace Google API panels with GCP Cloud Monitoring graph

### What was changed
Replaced two Prometheus-based Google API panels (bargauge + timeseries using app-instrumented `google_api_requests_total`) with a single "Google API Requests Per Day" timeseries panel that pulls directly from Google Cloud Monitoring.

**Files modified:**
- `.github/workflows/deploy.yml` — injects `GCP_MONITORING_KEY` into `app-secrets`
- `deploy/k8s/observability/grafana-dep.yaml` — mounts `app-secrets/GCP_MONITORING_KEY` as `/etc/grafana/gcp-credentials.json`
- `deploy/k8s/observability/grafana-datasources-cm.yaml` — adds `stackdriver` datasource (uid: `gcm`, project: `activitiesapp-490113`)
- `deploy/observability/local/grafana-datasources.yaml` — same for local compose
- `deploy/observability/local/activitiesapp-observability-dashboard.json` — new panel + removed old `$google_environment`/`$google_api_type` variables
- `deploy/k8s/observability/grafana-dashboards-cm.yaml` — synced

### New panel design
Two series in one timeseries panel:
- **Series A (Requests):** `serviceruntime.googleapis.com/api/request_count`, ALIGN_DELTA, +86400s, REDUCE_SUM, filter: `resource.labels.service = maps.googleapis.com`
- **Series B (Daily Limit):** `serviceruntime.googleapis.com/quota/limit`, ALIGN_MEAN, +86400s, REDUCE_MAX, same filter. Naturally absent before ~Apr 7 when the 143/day limit was set.

### Manual prerequisite (user must do before deploying)
1. GCP Console → IAM → Create service account `grafana-monitoring-reader` with `Monitoring Viewer` role
2. Download JSON key
3. GitHub repo → Settings → Secrets → add `GCP_MONITORING_KEY` = full JSON file content

### Known risk
The `quota/limit` metric's `quota_metric` label value for Maps API is not confirmed. If the Daily Limit series returns no data, open Grafana → Edit panel → Metrics Explorer to find the exact label values for `serviceruntime.googleapis.com/quota/limit` filtered to `maps.googleapis.com`.


## 2026-04-22 — Weekly TLS cert renewal CronJob

### What was added
New `deploy/k8s/certs/` manifests:
- `cert-renew-sa.yaml` — ServiceAccount `cert-renewer` + Role + RoleBinding restricted
  to `get/update/patch` on resourceNames `apex-activor-tls` and `wildcard-activor-tls`
- `cert-renew-cm.yaml` — ConfigMap `cert-renew-script-config` with `renew.sh`
  (installs certbot + certbot-dns-duckdns + kubectl at runtime, runs DNS-01 via
  DuckDNS, patches both TLS secrets)
- `cert-renew-cronjob.yaml` — CronJob `cert-renew` schedule `18 14 * * 3`,
  `timeZone: America/Denver`, single SAN cert covering `activor.duckdns.org` +
  `*.activor.duckdns.org`

`.github/workflows/deploy.yml` updated:
- `Create/update secrets` step appends `duckdns-secret` from GH repo secret `DUCKDNS_TOKEN`
- `Apply manifests` step applies `deploy/k8s/certs` after `data`

### Why CronJob instead of cert-manager
Professor ruling: "do not use the Cert Manager plugin... it can break things when
multiple student projects are using the same cluster." Cluster has cert-manager
installed (namespace `cert-manager`) with `ClusterIssuer/letsencrypt-prod` using
HTTP-01, but other tenants (cookbook, incrementum, survivors, cttanks) have
challenges stuck `invalid`. Plan creates zero cert-manager resources.

### Pattern reused
Mirrors working backup CronJob `deploy/k8s/data/postgres-backup-cronjob.yaml`.
Four crucial fields copied verbatim: `timeZone: America/Denver`,
`concurrencyPolicy: Forbid`, `jobTemplate.spec.backoffLimit: 0`,
`template.spec.restartPolicy: Never`. These were the lines identified from
commits `a1d0a59` + `0bbb578` as what made the backup cronjob actually fire.

### First-run trigger
CronJobs don't backfill. Manual kickoff once manifests applied:
`kubectl create job cert-renew-bootstrap --from=cronjob/cert-renew -n activitiesapp`

### Prerequisite
GitHub repo secret `DUCKDNS_TOKEN` added manually (confirmed 2026-04-22).

### Fix: DuckDNS single-TXT-record limitation
First kickoff failed with `unauthorized` / "Incorrect TXT record ..." for apex.
Root cause: DuckDNS allows only one TXT record per domain, but a SAN cert
covering `activor.duckdns.org` + `*.activor.duckdns.org` produces two ACME
challenges that both target `_acme-challenge.activor.duckdns.org`. The plugin
writes value A, then overwrites with value B — first challenge fails validation.

Resolution: issue two sequential certs instead of one SAN cert. `renew.sh` now
runs certbot twice (`--cert-name activor-apex` with `-d activor.duckdns.org`,
then `--cert-name activor-wildcard` with `-d *.activor.duckdns.org`). Each call
sets TXT, validates, and clears before the next call starts. No collision.
`apex-activor-tls` gets the apex cert; `wildcard-activor-tls` gets the wildcard
cert.

### Verification
- `kubectl get cronjob cert-renew -n activitiesapp` — SCHEDULE + TIMEZONE
- `openssl x509 -noout -enddate` on decoded `tls.crt` from both secrets
- `openssl s_client -servername <host> -connect <host>:443` for each ingress host



## 2026-04-17 — Core unit + integration tests

### What was added
Four new test files in `tests/ActivitiesApp.Core.Tests/`:

- **`HelpersTests.cs`** (37 tests) — covers ActivityFormatter, ZipCodeValidator, CategoryHelper,
  AddressBuilder, GeoCalculator, QuotaWarningHelper, ImageUrlResolver
- **`ActivityFilterServiceTests.cs`** (13 tests) — cost tiers (free/$/$$/$$), category,
  age range, location radius, FilterAndSortByTag (with/without location), Filter (search + filters combined)
- **`FuzzySearchServiceTests.cs`** (13 tests) — empty query, exact/partial name match,
  misspelling tolerance, synonym resolution (bar→Nightlife, hike→Outdoors, food→Restaurant+FastFood),
  score ordering, SearchOrAll
- **`FilterPipelineIntegrationTests.cs`** (7 tests) — integration tests: full pipeline of
  FuzzySearchService + ActivityFilterService + GeoCalculator + CategoryHelper working together
  end-to-end against a realistic 13-activity dataset

### Key findings
- `"trail"` fuzzy-matches `"thai"` (Restaurant keyword) at similarity 0.6 — intentional engine behavior
- `"food"` fuzzy-matches `"woods"` (Outdoors keyword) at similarity 0.6 — intentional engine behavior
- Pre-existing failure: `LocationService_ManualOverrideTakesPriority_AndErrorClearsGpsState` (unrelated)

### Result
96/97 pass (1 pre-existing failure). Total Core test count raised from 4 → 70+ tests.



## 2026-04-13 — 401 audience error diagnostics + fix

### Problem
Creating an activity returns:
> `401 — Wrong audience. The audience '(null)' is invalid`

The token's `aud` claim is null/missing when it reaches the API.

### Root Cause
The Azure App Registration for `6d3dc4ee-33ce-4656-95c8-702a38464687` likely does not have
"Expose an API" properly configured. Without the Application ID URI set to
`api://6d3dc4ee-33ce-4656-95c8-702a38464687` and the `access_as_user` scope enabled,
Azure AD cannot issue a properly-scoped access token — it returns a token with a null/missing
`aud` claim instead of rejecting the request outright.

### Azure Portal Fix Required (manual steps)
1. Go to Azure Portal → Microsoft Entra ID → App Registrations → `6d3dc4ee-33ce-4656-95c8-702a38464687`
2. Click **"Expose an API"**
3. Set **Application ID URI** to exactly: `api://6d3dc4ee-33ce-4656-95c8-702a38464687`
4. Click **"Add a scope"**:
   - Scope name: `access_as_user`
   - Who can consent: Admins and users
   - State: Enabled
5. Click **"Authentication"** → verify redirect URIs include all active environments
6. If using a `ClientSecret`, verify it hasn't expired under **"Certificates & secrets"**

### Code Changes Made
- `src/ActivitiesApp.Web/Services/ActivityRestClient.cs`:
  - Added `BuildErrorMessage` / `Diagnose401` / `ExtractWwwAuthParam` / `Truncate` helpers
  - `CreateActivityAsync` now: catches `MicrosoftIdentityWebChallengeUserException` for token
    acquisition failures; throws `InvalidOperationException` with 8-level 401 diagnosis +
    raw `WWW-Authenticate` header appended to every branch
- `src/ActivitiesApp.Shared/Pages/CreateActivity.razor`:
  - Catches `InvalidOperationException` first and shows `ex.Message` directly in the UI
  - `HttpRequestException` and generic `Exception` also now show `ex.Message` / type info
    instead of the opaque "Failed to create activity" fallback
