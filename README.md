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
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/postgres-pvc.yaml -f k8s/postgres-dep.yaml -f k8s/postgres-svc.yaml
kubectl apply -f k8s/otel-collector-configmap.yaml -f k8s/otel-collector-dep.yaml -f k8s/otel-collector-svc.yaml
kubectl apply -f k8s/prometheus-configmap.yaml -f k8s/prometheus-dep.yaml -f k8s/prometheus-svc.yaml -f k8s/prometheus-ingress.yaml
kubectl apply -f k8s/loki-configmap.yaml -f k8s/loki-dep.yaml -f k8s/loki-svc.yaml
kubectl apply -f k8s/tempo-configmap.yaml -f k8s/tempo-dep.yaml -f k8s/tempo-svc.yaml
kubectl apply -f k8s/grafana-datasources-cm.yaml -f k8s/grafana-dashboard-provider-cm.yaml -f k8s/grafana-dashboard-cm.yaml -f k8s/grafana-dep.yaml -f k8s/grafana-svc.yaml -f k8s/grafana-ingress.yaml
kubectl apply -f k8s/api-dep.yaml -f k8s/api-svc.yaml
kubectl apply -f k8s/web-dep.yaml -f k8s/web-svc.yaml -f k8s/web-ingress.yaml
```

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

The five most useful metrics implemented in the provisioned Grafana dashboard are:

- Application performance: p95 latency by route from Prometheus HTTP server histograms
- API response success rate: 2xx vs 4xx/5xx request rates by service and status code
- Error count and messages: Loki-backed warning/error volume plus recent error messages
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
