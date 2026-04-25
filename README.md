# ActivitiesApp

ActivitiesApp is a platform for discovering and organizing local activities. The idea behind it is simple: finding things to do near you is fragmented — businesses list events on their own sites, pickup games get organized over text, and local competitions never reach the people who'd love to join. ActivitiesApp pulls all of that into one place. Businesses, organizers, and everyday users can all post what's happening, and anyone looking for something to do can browse, filter, and join from a single app.

## What the App Does

When you open ActivitiesApp, it uses your location to surface nearby activities — pulling in real businesses and venues from Google Places alongside community-posted events. You can browse everything at once or filter by category to find exactly what you're looking for.

**Features:**

- **Discover nearby activities** — the app queries Google Places in real time and blends those results with user-created events, sorted by distance from your current location
- **Browse by category** — filter by tags like sports, food, music, arts, outdoor, competitions, and more
- **Fuzzy search** — search by name or description with typo-tolerant matching
- **Create events** — authenticated users can post their own activities with a name, description, location, time, cost, age range, and category (pickup basketball game, local trivia night, hiking meetup, etc.)
- **Edit and delete your own posts** — full ownership over events you created
- **User profiles** — display name and profile picture shown on activities you've posted
- **Photo browsing** — venue photos sourced from Google Places, served through a caching proxy to keep the app fast
- **Offline support** — the mobile app caches activity data locally and syncs changes when connectivity returns
- **Age filtering** — every activity has a min/max age range so the right people find the right events
- **Web + mobile** — a Blazor web app and a .NET MAUI mobile app (Android, iOS, Windows) share the same backend
- **Sign in with Microsoft** — authentication handled by Microsoft Entra ID (Azure AD), no passwords stored

## How Activity Data Works

Activities come from two sources that are merged together:

1. **Google Places** — when you search nearby, the API calls Google Places in the background and automatically creates activity records from matching venues. Results are cached for an hour so repeated searches don't burn through API quota. Photos are cached for 24 hours.

2. **User-created activities** — signed-in users can post anything: a pickup game, a community event, a competition. These live in the database permanently and show up alongside the Google results.

Both sources feed into the same activity model. When you look at the app, you can't tell which came from where — it's all just things to do near you.

## Architecture

The solution is made up of several projects that run together:

| Part | What it is |
|---|---|
| **API** | ASP.NET Core backend — handles all data, Google Places calls, auth, and photo proxying |
| **Web** | Blazor Server app — the browser-based frontend, talks to the API |
| **Mobile** | .NET MAUI app — runs natively on Android, iOS, and Windows |
| **Database** | PostgreSQL 16 — primary data store in production |
| **Document store** | Azure Cosmos DB — original data source, used to seed Postgres on first boot |
| **Auth** | Microsoft Entra ID — OIDC sign-in for web and mobile |
| **Maps** | Google Maps / Places API — nearby search, geocoding, and venue photos |

In production everything runs on Kubernetes. For local development the same services run in Docker Compose.

---

## CI / CD — How Code Gets to Production

Every change to the `master` branch automatically triggers a pipeline that builds, tests, and deploys the app. Here is what happens, step by step.

### The Pipeline

```
lint → build + test → integration tests → build images → deploy
```

**Step 1 — Lint:** A runner checks that all code is formatted correctly using `dotnet format`. If anything is off, the pipeline stops here.

**Step 2 — Build + Test:** A runner runs all unit tests. These test individual pieces of logic in isolation.

**Step 3 — Integration tests:** A runner runs the integration tests, which run after unit tests pass. Unlike unit tests, these hit a real database — no mocks — so they catch problems that would only show up at the boundary between the app and Postgres.

**Step 4 — Build images:** Two runners build Docker container images in parallel — one for the API, one for the web app. Each image is tagged with the short Git commit hash so every deploy is traceable back to exact code. Images are pushed to Docker Hub. The runners use a layer cache stored in GitHub Actions to keep builds fast.

**Step 5 — Deploy:** A self-hosted runner (a machine we control that sits next to the Kubernetes cluster) takes over here. Cloud runners can't reach the cluster directly, so this runner handles everything that requires cluster access: applying Kubernetes manifests, running database migrations, and rolling out the new images.

### What the Deploy Runner Does

The deploy runner runs through these steps in order:

1. Applies all Kubernetes manifests (namespace, database, certificates, app, observability)
2. Creates or updates Kubernetes Secrets from GitHub Actions secrets (Postgres password, Cosmos key, Google Maps key, Azure client secret, etc.)
3. Runs database migrations as a Kubernetes Job — the new API image runs `--migrate-only`, applies any pending EF Core migrations, and exits before the new version starts serving traffic
4. Starts a k6 smoke test in the background, pointed at `https://activor.duckdns.org`, writing results directly into Prometheus
5. Updates both deployments to the new image tag and waits for the rollout to complete
6. Waits for k6 to finish — if the smoke test fails, the pipeline fails
7. Prints a final status summary of pods, services, and ingress

If anything in this pipeline fails, a push notification is sent via ntfy.

### Pull Request Environments

Every pull request also gets its own pipeline run — a separate workflow that runs lint, unit tests, integration tests, and builds images. A bot posts a comment on the PR with the environment status.

### Certificate Renewal

A Kubernetes CronJob runs on a schedule to renew the TLS certificate for `activor.duckdns.org` via DuckDNS. This happens automatically — no manual intervention needed.

---

## Observability — How the App Is Monitored

The full monitoring stack runs in the same Kubernetes cluster as the app (and identically in Docker Compose locally). Both the API and the web app emit telemetry automatically — metrics and logs flow out of the app through a collector and into dashboards without any manual steps.

### Where Each Piece Lives

| Component | Where it runs | What it does |
|---|---|---|
| **OTel Collector** | Kubernetes pod (`activitiesapp` namespace) | Receives all telemetry from the app over gRPC, fans it out to Prometheus and Loki |
| **Prometheus** | Kubernetes pod (`activitiesapp` namespace) | Scrapes metrics from the OTel Collector, stores time-series data, receives k6 smoke test results |
| **Loki** | Kubernetes pod (`activitiesapp` namespace) | Stores structured logs from both services |
| **Grafana** | Kubernetes pod (`activitiesapp` namespace) | Reads from Prometheus, Loki, and GCP Cloud Monitoring — displays the observability dashboard |
| **Uptime Kuma** | Kubernetes pod (`activitiesapp` namespace) | Uptime monitoring and alerting |

### How Telemetry Flows

```
API + Web app  →  OTel Collector  →  Prometheus  (metrics)
                                  →  Loki        (logs)
                                         ↓
                                      Grafana     (dashboards)
                                         ↑
                                  GCP Cloud Monitoring  (Google Maps API usage)
```

Both apps are configured with `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the OTel Collector. They push telemetry over gRPC on port 4317. The collector routes metrics to Prometheus and logs to Loki. Grafana reads from all three sources and assembles the dashboard.

### The Grafana Dashboard

The dashboard is provisioned automatically from a config file — Grafana loads it on startup with no manual import needed. Grafana does not use persistent storage; the dashboard lives in a Kubernetes ConfigMap.

| Panel | What it shows |
|---|---|
| HTTP p95 latency by route | How long the slowest 5% of requests take, broken down by API route |
| Request success rate | The ratio of successful (2xx) to failed (4xx/5xx) responses, per service |
| HTTP 5xx count | Count of server errors — turns red when non-zero |
| Warning log volume | Count of warning-level log lines over time, sourced from Loki |
| Activity creation rate | Custom metric: how many activities are being created per minute |
| Live log stream | A live tail of recent API and web app logs from Loki |
| Concurrent users | Active connections over time |
| GCP API usage | Google Maps / Places API request rates pulled directly from GCP Cloud Monitoring |

### Dashboard Persistence

Because Grafana runs without a PVC, any edits made in the UI are lost when the pod restarts. To make changes permanent:

1. Edit the dashboard in Grafana.
2. Use the Export option to download the dashboard JSON.
3. Replace `deploy/observability/local/activitiesapp-observability-dashboard.json`.
4. Replace the JSON in `deploy/k8s/observability/grafana-dashboards-cm.yaml`.
5. Re-apply the ConfigMap and restart the Grafana deployment, or re-run `docker compose up -d grafana` locally.

---

## Running Locally

```bash
docker compose up -d
```

| Service | URL |
|---|---|
| Web app | http://localhost:8081 |
| Grafana | http://localhost:3000 |
| Prometheus | http://localhost:9090 |
| Loki | http://localhost:3100 |
| OTel Collector metrics | http://localhost:8889/metrics |

## Kubernetes — Manual Apply

If you need to apply manifests by hand (the deploy runner does this automatically on every push):

```bash
kubectl apply -f deploy/k8s/base/namespace.yaml
kubectl apply -f deploy/k8s/data
kubectl apply -f deploy/k8s/certs
kubectl apply -f deploy/k8s/app
kubectl apply -f deploy/k8s/observability
```

Create the TLS secret before applying the web ingress:

```bash
kubectl create secret tls activor-duckdns-tls \
  --cert=fullchain.pem \
  --key=privkey.pem \
  -n activitiesapp
```

Ingress hosts:

| Host | Service |
|---|---|
| `activor.duckdns.org` | Web app (HTTP redirects to HTTPS) |
| `grafana.activor.duckdns.org` | Grafana |
| `prometheus.activor.duckdns.org` | Prometheus |

Microsoft Entra redirect URI: `https://activor.duckdns.org/signin-oidc`

Port-forward fallback if ingress isn't ready:

```bash
kubectl port-forward svc/activitiesapp-web 8081:80 -n activitiesapp
kubectl port-forward svc/grafana 3000:3000 -n activitiesapp
kubectl port-forward svc/prometheus 9090:9090 -n activitiesapp
```

## Known Issues

- **Port conflicts** — the Compose stack does not publish Postgres or the API to fixed host ports to avoid conflicts on shared machines.
- **Cold start with empty Grafana panels** — Prometheus needs at least one scrape interval of data. If panels are empty after a fresh start, browse the app to generate traffic, then reload the dashboard.
- **API starts before migrations complete** — if this happens the API will create the missing tables from the EF model so it stays up and keeps emitting telemetry. Migrations will catch up on the next normal start.
- **Kubeconfig misconfiguration** — if `kubectl` commands return HTML or OpenAPI errors, the cluster context is wrong. Check `kubectl config current-context` and `kubectl cluster-info`, then use port-forward as a fallback while fixing it.
