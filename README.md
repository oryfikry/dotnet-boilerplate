# dotnet-pbac-boilerplate

.NET 10 Minimal API boilerplate following the **Vertical Slice Architecture**
detailed in [`prd.md`](./prd.md) (v2.1).

## Status

| Milestone | Scope | State |
| --- | --- | --- |
| **M1** | Foundation & Bootstrapping (skeleton, IEndpoint, ProblemDetails, ApiResponse) | ✅ |
| M2 | CQRS & Data Layer (MediatR, EF Core, Dapper) | ⏳ |
| M3 | Resilience & Security (Redis cache, JWT, permissions) | ⏳ |
| M4 | Pure Integration Testing (Testcontainers) | ⏳ |
| M5 | Observability & DevOps (OpenTelemetry, Serilog, Dockerfile) | ⏳ |
| M6 | (Stretch) AOT profile | ⏳ |

## Prerequisites

* [.NET SDK 10.0.300](https://dotnet.microsoft.com/download) (pinned in `global.json`)
* Docker Desktop (only needed from M2 onward, for Postgres/Redis)

## Quickstart

```powershell
dotnet restore
dotnet build
dotnet run --project src\Api
```

Then probe the live skeleton:

```powershell
# Liveness
curl http://localhost:5000/health/live

# API info
curl http://localhost:5000/api/v1

# OpenAPI document (Development only)
curl http://localhost:5000/openapi/v1.json
```

> The default URL is printed on startup; .NET 10 typically uses port 5000
> (HTTP) and 5001 (HTTPS) unless `launchSettings.json` overrides them.

## Local Infrastructure

```powershell
docker compose up -d   # starts Postgres + Redis
docker compose down    # tears down (volumes persist)
```

Currently no service depends on these — they will be wired in **Milestone 2**.

## Repository Layout

See [`prd.md` §5](./prd.md#5-peta-direktori-vertikal-directory-map) for the
canonical directory map. Quick reference:

```
src/Api/
├── Common/         # Shared kernels (Endpoints, Exceptions, Responses, Context, ...)
├── Infrastructure/ # System engines (added in M2-M5)
├── Features/       # Vertical slices (one folder per feature)
└── Program.cs
```

## Adding a Feature (Slice)

Use the canonical `CreateProduct` slice in [`prd.md` §7](./prd.md#7-canonical-slice-example-createproduct)
as the template. Every slice must satisfy the
[Definition of Done in §8](./prd.md#8-definition-of-done-dod-per-slice) before it is merged.

## Conventions Recap

* **VSA strict** — no `Controllers/` or `Repositories/` directories.
* **EF Core** for commands, **Dapper** for queries (see PRD §6 directive #3).
* **No mocks** — integration tests use Testcontainers (PRD §6 directive #4).
* **RFC 7807** for all error responses; `ApiResponse<T>` only for the success path.
* **Permission-based authorization** via `.RequirePermission("...")` (PRD §4.1).

## License

TBD.
