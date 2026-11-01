# Technical overview

## Solution structure

The solution `NewtonsBiograf.sln` (.NET 8) contains four projects. Introduced by US-116.

| Project | Type | Purpose |
|---|---|---|
| `src/NewtonsBiograf.Api` | ASP.NET Core Web API (controllers) | HTTP API, Swagger (Development only), CORS policy `Client` |
| `src/NewtonsBiograf.Client` | Blazor WebAssembly | Staff and customer web app; named `HttpClient` using `ApiBaseUrl` |
| `src/NewtonsBiograf.Shared` | Class library | DTOs and enums shared by API, client and tests; no EF dependency |
| `tests/NewtonsBiograf.Tests` | xUnit | Automated tests; uses `WebApplicationFactory` from `Microsoft.AspNetCore.Mvc.Testing` |

Controllers are used instead of minimal APIs so that `[Authorize(Policy=...)]` can be applied declaratively.

## Configuration
| Key | File | Default | Purpose |
|---|---|---|---|
| `Cors:ClientOrigin` | `src/NewtonsBiograf.Api/appsettings.json` | `https://localhost:5001` | Origin allowed by the `Client` CORS policy |
| `ApiBaseUrl` | `src/NewtonsBiograf.Client/wwwroot/appsettings.json` | `https://localhost:7000/` | Base address of the API for the named `HttpClient` |

## API endpoints
| Method | Route | Auth | Response |
|---|---|---|---|
| GET | `/api/health` | Anonymous | 200 `{ "status": "ok" }` |

## Known gaps
- Build and test results for US-116 are not yet verified; no checks are configured on the platform.
