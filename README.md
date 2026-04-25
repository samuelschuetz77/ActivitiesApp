# ActivitiesApp

A full-stack .NET 10 application for discovering and tracking activities. The solution includes a REST API, a Blazor web frontend, and a .NET MAUI mobile app targeting Android, iOS, and Windows.

## Architecture

| Layer | Technology |
|---|---|
| API | ASP.NET Core minimal API |
| Web | Blazor Server |
| Mobile | .NET MAUI (Android / iOS / Windows) |
| Database | PostgreSQL 16 with EF Core migrations |
| Document store | Azure Cosmos DB |
| Maps | Google Maps / Places API |
| Auth | Microsoft Entra ID (OIDC) |
| Runtime | Kubernetes (self-hosted) + Docker Compose (local) |

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

Quick health checks:

```bash
docker compose ps
curl http://localhost:8081/api/version
curl http://localhost:9090/api/v1/targets
```

## CI / CD

Every push to `master` runs through a GitHub Actions pipeline:

```
lint → unit tests → integration tests → build & push images → deploy to Kubernetes
```

- **Lint** — `dotnet format --verify-no-changes` on both projects
- **Unit tests** — all test projects except integration
- **Integration tests** — run after unit tests pass; hit a real database (no mocks)
- **Images** — built with Docker Buildx, tagged by short SHA, pushed to Docker Hub with GitHub Actions layer cache
- **Deploy** — runs on a self-hosted runner with `kubectl` access; applies all manifests in dependency order, runs EF migrations as a Kubernetes Job, then does a rolling deploy

**PR environments** — each pull request gets its own build, lint, and test run via a separate workflow. A bot comments the environment URL on the PR.

**Smoke test** — `k6` runs against `https://activor.duckdns.org` during the rolling deploy. k6 writes results directly to Prometheus via the `experimental-prometheus-rw` output. The deploy fails if k6 exits non-zero.

**Failure notifications** — any pipeline failure sends a push notification via `ntfy`.

## Kubernetes

### Applying Manifests

```bash
kubectl apply -f deploy/k8s/base/namespace.yaml
kubectl apply -f deploy/k8s/data
kubectl apply -f deploy/k8s/certs
kubectl apply -f deploy/k8s/app
kubectl apply -f deploy/k8s/observability
```

### TLS

The web ingress terminates TLS using a Kubernetes secret named `activor-duckdns-tls`. Create it before applying the ingress:

```bash
kubectl create secret tls activor-duckdns-tls \
  --cert=fullchain.pem \
  --key=privkey.pem \
  -n activitiesapp
```

A CronJob in `deploy/k8s/certs/` handles automated certificate renewal via DuckDNS.

Ingress hosts:

| Host | Service |
|---|---|
| `activor.duckdns.org` | Web app (HTTPS, HTTP → HTTPS redirect) |
| `grafana.activor.duckdns.org` | Grafana |
| `prometheus.activor.duckdns.org` | Prometheus |

Microsoft Entra redirect URI: `https://activor.duckdns.org/signin-oidc`

### Port-Forward Fallback

```bash
kubectl port-forward svc/activitiesapp-web 8081:80 -n activitiesapp
kubectl port-forward svc/grafana 3000:3000 -n activitiesapp
kubectl port-forward svc/prometheus 9090:9090 -n activitiesapp
```

## Observability

The full observability stack runs identically in Docker Compose and Kubernetes. Both services (`activitiesapp-api`, `activitiesapp-web`) export OTLP telemetry over gRPC to the OTel Collector, which fans it out to Prometheus and Loki.

### Signal Pipeline

```
App (OTLP/gRPC) → OTel Collector → Prometheus  (metrics)
                                 → Loki         (logs)
                                 → Grafana      (dashboards)
```

### Grafana Dashboard — ActivitiesApp Observability

The dashboard is fully provisioned from config files (no PVC required). It covers:

| Panel | What it shows |
|---|---|
| HTTP p95 latency by route | Tail latency from Prometheus `http_server_request_duration_seconds` histograms |
| Request success rate | 2xx vs 4xx/5xx rates per service and status code |
| HTTP 5xx count | Critical error count, stat panel with red threshold |
| Warning log volume | Loki query counting `level=warn` log lines per interval |
| Activity creation rate | Custom Prometheus counter emitted by the API on each created activity |
| Live log stream | Loki log panel streaming recent API and web logs |
| Concurrent users | Active session or connection count over time |
| GCP API usage | Cloud Monitoring data for Google Maps / Places API request rates |

Dashboard sources:

- Docker Compose: `deploy/observability/local/activitiesapp-observability-dashboard.json`
- Kubernetes: `deploy/k8s/observability/grafana-dashboards-cm.yaml` (same JSON embedded in a ConfigMap)

To edit the dashboard and keep it persistent:

1. Edit panels in Grafana UI.
2. Export dashboard JSON.
3. Replace the JSON in both files above.
4. Restart Grafana (local) or re-apply the ConfigMap and rollout restart (Kubernetes).

### Kubernetes Grafana — GCP Cloud Monitoring

The Kubernetes Grafana instance also connects to GCP Cloud Monitoring via a service-account JWT datasource (`gcp-grafana-secrets`). This lets dashboards query Google Maps / Places API usage directly from Cloud Monitoring without a separate exporter.

### Known Issues

- Host port conflicts are common on shared machines. The Compose stack does not publish Postgres or the API to fixed host ports to avoid this.
- If the API starts before EF migrations complete it will create missing tables directly from the model so telemetry keeps flowing.
- If API-specific Grafana panels are empty after a fresh start, generate traffic by browsing the app before reading the dashboard — Prometheus needs at least one scrape interval of data.
- If `kubectl` commands return HTML or OpenAPI errors the cluster context is misconfigured — check `kubectl config current-context` and `kubectl cluster-info`, then use port-forward as fallback.
