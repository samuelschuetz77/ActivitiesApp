# CLAUDE.md - ActivitiesApp Project Instructions

## Project Overview
.NET Aspire solution: gRPC API + MAUI Mobile App + Blazor Web frontend.
Databases: Azure Cosmos DB (primary), PostgreSQL (secondary).
Deployment: Azure App Service + Kubernetes via GitHub Actions.

## Kanban Tracking
All project kanban items are tracked in [`KANBAN.md`](KANBAN.md) at the repo root.

**Before starting work:** Check `KANBAN.md` to see if the current task matches a kanban item.
**After completing work that matches a kanban item:**
1. Update the item's status in `KANBAN.md` (move it to the correct section, check the box)
2. Tell the user: "This completes the kanban item **[item name]**. Don't forget to move it to Done on the GitHub Projects board."

## Key Conventions
- Docker image tags must be specific (git SHA or version), NEVER use `:latest`
- Always read/update `troubleshooting/iteration-log.md` each conversation
- Tests run via `dotnet test` from repo root
- Linting: `dotnet format --verify-no-changes`
