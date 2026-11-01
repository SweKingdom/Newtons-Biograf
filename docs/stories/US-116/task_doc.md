# US-116: Create solution structure and remove boilerplate
**Status:** Ready for UAT · **Category:** devops · **Points:** 3 · **Sprint:** Not specified

## Summary
The repository now holds a clean .NET 8 solution with four projects: an API, a Blazor WebAssembly client, shared contracts and an xUnit test project. The Blazor template pages, sample navigation, default styling and sample data have been removed. The client home page shows the API health status as a connectivity check.

## What was built
- `NewtonsBiograf.sln`: solution with 4 projects (Api, Client, Shared, Tests).
- `src/NewtonsBiograf.Api`: ASP.NET Core Web API (controllers). `Program.cs` registers controllers, Swagger (Development only, JWT bearer scheme), the CORS policy `Client` (origin from `Cors:ClientOrigin`) and maps controllers.
- `HealthController` (`src/NewtonsBiograf.Api/Controllers/HealthController.cs`): `GET /api/health`, anonymous, returns `{ "status": "ok" }`.
- `src/NewtonsBiograf.Client`: Blazor WebAssembly client. Registers a named `HttpClient` with base address from `ApiBaseUrl`. `Pages/Home.razor` calls `/api/health` to show "API status: ok". `Layout/MainLayout.razor` and `App.razor` are the minimal shell.
- `src/NewtonsBiograf.Shared`: class library with DTOs only (`HealthResponse`). No Entity Framework dependency.
- `tests/NewtonsBiograf.Tests`: xUnit project with `Microsoft.AspNetCore.Mvc.Testing`. `HealthTests` covers the health endpoint. The API exposes `public partial class Program {}` for test access.
- `.gitignore`: standard dotnet ignores. The committed SQLite file is not ignored.

## How it works
- **AC 1 (solution builds):** the solution contains Api, Client, Shared and Tests, all targeting net8.0. Build result: not yet verified (see Known limitations).
- **AC 2 (boilerplate removed):** Counter, Weather, FetchData, the sample navigation, default `app.css`/Bootstrap and `sample-data` are removed. A search of the repository for these names finds no source matches.
- **AC 3 (API and client run):** the API starts with Swagger UI in Development only. `GET /api/health` returns 200 with `{ "status": "ok" }` without authentication. The client reads `ApiBaseUrl` from `wwwroot/appsettings.json` and the API allows that origin through the `Client` CORS policy.

## Configuration
| Key | File | Value |
|---|---|---|
| `Cors:ClientOrigin` | `src/NewtonsBiograf.Api/appsettings.json` | `https://localhost:5001` |
| `ApiBaseUrl` | `src/NewtonsBiograf.Client/wwwroot/appsettings.json` | `https://localhost:7000/` |

## How to test
**Checks:** `dotnet build NewtonsBiograf.sln` and `dotnet test NewtonsBiograf.sln`. No automated checks are configured on the platform.

- **AC 1:** run `dotnet build NewtonsBiograf.sln`. **Verify:** succeeds with Api, Client, Shared, Tests.
- **AC 2:** search the repository for `Counter`, `Weather`, `FetchData`, `bootstrap`, `sample-data`. **Verify:** no matches in source; no template CSS or pages exist.
- **AC 3:** run `dotnet test NewtonsBiograf.sln` (runs `HealthTests`). Then run `dotnet run --project src/NewtonsBiograf.Api` and open https://localhost:7000/swagger. Then run `dotnet run --project src/NewtonsBiograf.Client` and open https://localhost:5001. **Verify:** Swagger loads in Development; Home shows "API status: ok".

## Deployment notes
- No database, migration or pre/post-deployment steps.
- Before any non-local deployment, set `Cors:ClientOrigin` on the API and `ApiBaseUrl` in the client to the real hosts.

## Known limitations
- The build and the `HealthTests` result are not verified. The developer fixed two compile errors in the Tests project (missing `Xunit` global using in `NewtonsBiograf.Tests.csproj`; missing `using Microsoft.AspNetCore.Hosting;` in `HealthTests.cs`). Neither `dotnet build` nor `dotnet test` has been run on the branch, because no dotnet runtime was available and no checks are configured.
- The manual Swagger and client Home checks (AC 3) have not been performed.
- CI pipeline is out of scope.
- Feature code is out of scope; the solution only provides the structure.
