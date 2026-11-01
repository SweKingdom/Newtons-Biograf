# US-116 — Test report (iteration 1)
**Verdict:** ❌ Changes requested · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Solution builds | ❌ | Tests project has compile errors (static review) |
| AC 2 Boilerplate removed | ✅ | File list and file contents contain no template files or terms |
| AC 3 API and client run | ❌ | Implementation looks right, but the proving tests don't compile |
| run_checks | ⛔ | No checks configured; dotnet could not be run by QA |

## Checks
### AC 1 — ❌
Both errors are in the Tests project, found by static review only. No `dotnet build` was run, by the developer or by QA.
- `NewtonsBiograf.Tests.csproj` has no `<Using Include="Xunit" />`. `[Fact]` in `HealthTests.cs` gives CS0246.
- `HealthTests.cs` calls `b.UseEnvironment(environment)` without `using Microsoft.AspNetCore.Hosting;`. This gives CS1061.

### AC 2 — ✅
No Counter, Weather, FetchData, NavMenu, sample-data, app.css or bootstrap in the file list or in the files read.

### AC 3 — ❌
HealthController, Program.cs (Swagger in Development only, bearer scheme, CORS policy "Client"), launchSettings ports and the client's named HttpClient all match the spec. The tests (health 200, Swagger Dev/Prod, CORS) are well designed, but they don't compile, so the AC is unproven. The manual check was not run.

## What to do
- Add `<Using Include="Xunit" />` to the Tests csproj and `using Microsoft.AspNetCore.Hosting;` to HealthTests.cs.
- Run `dotnet build NewtonsBiograf.sln` and `dotnet test NewtonsBiograf.sln`, and fix any further errors.

## Not tested
- Manual Swagger and client run (no runtime available).
