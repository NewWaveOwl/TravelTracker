# TravelTracker

## Milestone 2 planning

[Issue #9 planning deliverables](docs/milestone-2/issue-9/README.md) include the GitHub MCP setup, reviewed [attraction search specification #15](https://github.com/NewWaveOwl/TravelTracker/issues/15), triage, priming interview and implementation plan. The workspace MCP configuration is shared without credentials; each teammate signs in with their own account.

## Projects

- `TravelTracker.Api` — ASP.NET Core entry point and future controllers, contracts, and services.
- `TravelTracker.Data` — future contexts, repositories, migrations, and seed data.
- `TravelTracker.Domain` — future domain models.

## Build

```powershell
dotnet build TravelTracker.slnx
```

## Run the empty API

```powershell
dotnet run --project src/TravelTracker.Api
```
