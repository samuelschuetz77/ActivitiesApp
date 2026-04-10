# Project Kanban Tracker

This file tracks all kanban items for the Final Project RTW submission (due Apr 18, 2026).
**GitHub Projects board must stay in sync with this file.**

When working on any item below, after completing it:
1. Update its status in this file from `NOT STARTED` or `IN PROGRESS` to `DONE`
2. Remind the user to move the matching card on the GitHub Projects kanban board

---

## DONE

- [x] **CI/CD Pipeline** (10 pts) — `deploy.yml` with lint, test, build, deploy on every push to master
- [x] **Azure App Service (API + Web)** — Two App Services deployed (Activities-API, 2-Blazor)
- [x] **Azure Cosmos DB** — Primary database with ETag concurrency, partition key on City
- [x] **Azure Key Vault** — Secrets managed via GitHub Actions secrets
- [x] **Mobile App: 4 Pages** — Home, Activities, Create, Profile (tab-based shell)
- [x] **API Unit Tests** (8 tests) — GooglePlaceTagMapper tests in `tests/ActivitiesApp.Api.Tests/`
- [x] **Infrastructure Unit Tests** (9 tests) — ActivityModel tests in `tests/ActivitiesApp.Infrastructure.Tests/`
- [x] **User Auth (JWT + Identity)** — Register/login REST endpoints
- [x] **Google Places API Integration** — Places search, geocoding
- [x] **gRPC Service** — ActivityGrpcService for mobile-backend communication
- [x] **Observability Stack** — OTEL, Prometheus, Loki, Tempo, Grafana deployed

## IN PROGRESS

- [ ] **Significant Features Enabling Core Business Functionality** (100 pts) — Activities CRUD exists; needs polish, complete flows, edge cases
- [ ] **Mobile App: Professional Look & Feel** (50 pts) — Needs consistent spacing, images, animations, balanced layout

## NOT STARTED

- [ ] **Mobile App: Unit Tests for Non-Trivial Logic** (20 pts) — Need tests for ViewModels, services, page logic
- [ ] **WriteUp: Work Log** (50 pts) — Excel spreadsheet: Date, Name, Goal, Start, Result, Stop, Elapsed
- [ ] **WriteUp: PivotTable Summary** (20 pts) — Pivot by name, sum elapsed, with date/goal/result details
- [ ] **WriteUp: User Test Round 1** (20 pts) — 3+ target users, multiple alternative designs, record feedback
- [ ] **WriteUp: User Test Round 2** (20 pts) — Show design changes from Round 1, 3+ users, record feedback
- [ ] **WriteUp: User Test Round 3** (20 pts) — Show design changes from Round 2, 3+ users, record feedback
- [ ] **WriteUp: User Test Round 4** (20 pts) — Show design changes from Round 3, 3+ users, record feedback
- [ ] **WriteUp: Cloud Services Used & Why** (10 pts) — App Service, Cosmos DB, Key Vault descriptions
- [ ] **WriteUp: Cloud Services Planned but Didn't Use** (5 pts) — What was dropped and why
- [ ] **WriteUp: Cloud Services Didn't Plan but Did Use** (5 pts) — What was added unexpectedly and why
- [ ] **WriteUp: Cloud Security Documentation** (10 pts) — DB access restrictions, secret management, auth details
- [ ] **WriteUp: Describe 3 Azure Services Used** (10 pts) — Summary with rationale

---

## Daily Plan (Apr 8–17 | Buffer: Apr 16 & 17)

| Day | Date | Focus | Points |
|-----|------|-------|--------|
| 1 | Tue Apr 8 | Significant Features + Work Log Setup | 150 |
| 2 | Wed Apr 9 | Significant Features (complete core flows) | 100 |
| 3 | Thu Apr 10 | Professional Look & Feel | 50 |
| 4 | Fri Apr 11 | Mobile Unit Tests + User Test Round 1 | 40 |
| 5 | Mon Apr 14 | User Test Rounds 2 & 3 + Design Iteration | 40 |
| 6 | Tue Apr 15 | User Test Round 4 + All Write-Ups + PivotTable | 75 |
| 7 | Wed Apr 16 | **BUFFER** — Catch up on incomplete tasks | — |
| 8 | Thu Apr 17 | **BUFFER** — Final review, polish, submit | — |
