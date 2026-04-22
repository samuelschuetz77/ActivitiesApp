## Observability Setup

This branch adds a local Docker Compose observability stack and matching Kubernetes manifests for:

- OpenTelemetry Collector
- Prometheus
- Loki
- Tempo
- Grafana

Grafana is provisioned from mounted files only. It does not use a PVC.

### Local URLs

- App web: `http://localhost:8081`
- Grafana: `http://localhost:3000`
- Prometheus: `http://localhost:9090`
- Loki: `http://localhost:3100`
- Tempo: `http://localhost:3200`
- Collector metrics: `http://localhost:8889/metrics`

### Local Start

Start the full stack:

```powershell
docker compose up -d
```

Useful checks:

```powershell
docker compose ps
curl.exe -s http://localhost:3000/api/search
curl.exe -s http://localhost:9090/api/v1/targets
curl.exe -s http://localhost:8081/api/version
```

### Kubernetes Manifests

Apply the app and observability manifests in `k8s/`.

Recommended order:

```powershell
kubectl apply -f deploy/k8s/base/namespace.yaml
kubectl apply -f deploy/k8s/data/postgres-pvc.yaml -f deploy/k8s/data/postgres-dep.yaml -f deploy/k8s/data/postgres-svc.yaml
kubectl apply -f deploy/k8s/observability/otel-collector-configmap.yaml -f deploy/k8s/observability/otel-collector-dep.yaml -f deploy/k8s/observability/otel-collector-svc.yaml
kubectl apply -f deploy/k8s/observability/prometheus-configmap.yaml -f deploy/k8s/observability/prometheus-dep.yaml -f deploy/k8s/observability/prometheus-svc.yaml -f deploy/k8s/observability/prometheus-ingress.yaml
kubectl apply -f deploy/k8s/observability/loki-configmap.yaml -f deploy/k8s/observability/loki-dep.yaml -f deploy/k8s/observability/loki-svc.yaml
kubectl apply -f deploy/k8s/observability/tempo-configmap.yaml -f deploy/k8s/observability/tempo-dep.yaml -f deploy/k8s/observability/tempo-svc.yaml
kubectl apply -f deploy/k8s/observability/grafana-datasources-cm.yaml -f deploy/k8s/observability/grafana-dashboard-provider-cm.yaml -f deploy/k8s/observability/grafana-dashboard-cm.yaml -f deploy/k8s/observability/grafana-dep.yaml -f deploy/k8s/observability/grafana-svc.yaml -f deploy/k8s/observability/grafana-ingress.yaml
kubectl apply -f deploy/k8s/app/api-dep.yaml -f deploy/k8s/app/api-svc.yaml
kubectl apply -f deploy/k8s/app/web-dep.yaml -f deploy/k8s/app/web-svc.yaml -f deploy/k8s/app/web-ingress.yaml
```

### TLS Without cert-manager

The web ingress is configured to terminate TLS with a normal Kubernetes TLS secret named `activor-duckdns-tls` and to redirect all HTTP traffic to HTTPS.

Create the TLS secret yourself before applying the ingress. Example:

```powershell
kubectl create secret tls activor-duckdns-tls `
  --cert=fullchain.pem `
  --key=privkey.pem `
  -n activitiesapp
```

After applying the manifests, verify TLS:

```powershell
kubectl get secret activor-duckdns-tls -n activitiesapp
kubectl get ingress -n activitiesapp
```

Expected result:

- `https://activor.duckdns.org` serves the certificate stored in `activor-duckdns-tls`
- `http://activor.duckdns.org` redirects to `https://activor.duckdns.org`
- Microsoft Entra sign-in uses `https://activor.duckdns.org/signin-oidc`

Ingress hosts:

- App: `activor.duckdns.org`
- Grafana: `grafana.activor.duckdns.org`
- Prometheus: `prometheus.activor.duckdns.org`

### Port-Forward Fallback

If DNS or ingress is not ready, use port-forwarding instead:

```powershell
kubectl port-forward svc/grafana 3000:3000 -n activitiesapp
kubectl port-forward svc/prometheus 9090:9090 -n activitiesapp
kubectl port-forward svc/activitiesapp-web 8081:80 -n activitiesapp
```

### Selected Dashboard Metrics

The most useful metrics implemented in the provisioned Grafana dashboard are:

- Application performance: p95 latency by route from Prometheus HTTP server histograms
- API response success rate: 2xx vs 4xx/5xx request rates by service and status code
- Critical errors: Prometheus-backed HTTP 5xx request count by service
- Warnings: Loki-backed warning volume, separate from errors
- Near real-time logs: live Loki log stream for the API and web app
- Activity creation rate: custom Prometheus counter emitted by the API for created activities

The dashboard is provisioned from:

- `observability/local/activitiesapp-observability-dashboard.json` for Docker Compose
- `k8s/grafana-dashboard-cm.yaml` for Kubernetes

If you want to edit the dashboard later in Grafana and keep it persistent without a PVC:

1. Open the dashboard in Grafana.
2. Make your panel changes.
3. Export the dashboard JSON.
4. Replace `observability/local/activitiesapp-observability-dashboard.json`.
5. Replace the JSON embedded in `k8s/grafana-dashboard-cm.yaml`.
6. Restart local Grafana or re-apply the Kubernetes configmap and Grafana deployment.

### Screenshot Checklist

Take screenshots of:

- Prometheus locally showing active targets or metric query results
- Prometheus on Kubernetes showing active targets or metric query results
- Grafana locally showing the `ActivitiesApp Observability` dashboard
- Grafana on Kubernetes showing the same dashboard

### Known Friction Points

- Host port conflicts are common on student machines. The local Compose stack intentionally does not publish Postgres or the API to fixed host ports.
- If the API starts before its relational schema exists, it now creates the missing tables directly from the EF model so the app can stay alive for telemetry collection.
- The Compose stack showed healthy web metrics during validation. If API-specific request panels are empty, generate traffic by browsing the app and exercising API-backed pages before taking screenshots.
- If `kubectl` validation fails with HTML or OpenAPI errors, the cluster context is misconfigured. Use `kubectl config current-context`, `kubectl cluster-info`, and port-forward as fallback while fixing ingress or kubeconfig.
