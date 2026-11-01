# US-143: Test scenario documentation
**Status:** Ready for UAT · **Category:** Documentation · **Points:** 2 · **Sprint:** n/a

## Summary
Reviewers can now see which test scenarios exist, what each one checks, and how to run them. The scenario list and run commands are in `docs/TESTING.md`, and the README has a "Testing" section that links to it. No application behaviour changed.

## What was built
- `docs/TESTING.md` (new): overview of the test project and helpers (including `FakeClock : TimeProvider`), `dotnet test` run commands (by class, method and scenario area), one row per test method, a coverage matrix, and maintenance rules.
- `README.md`: new "Testing" section linking to `docs/TESTING.md`.

## How it works
- **AC 1 (scenario list):** `docs/TESTING.md` section 3 lists every test method with its class and method name. Theory cases share the row of their method. Section 4 maps each required area to its tests. Scenario classes are `BookingScenarios`, `CancellationScenarios`, `ShowingOverlapScenarios` and `AccessScenarios`.
- **AC 2 (how to run):** `docs/TESTING.md` section 2 lists the `dotnet test` commands, including filters by class, by method and by scenario area. It also notes that `~AuthTests` also matches `ClientAuthTests`, and gives a trailing-dot filter to avoid that.

## How to test
- Run `dotnet test --list-tests` and compare the output with section 3 of `docs/TESTING.md`. Every test method must have a row, and every row must name an existing class and method.
- Run the filters in section 2, for example `dotnet test --filter "FullyQualifiedName~BookingScenarios"` (expect 3 tests) and `dotnet test --filter "FullyQualifiedName~Customer_can_cancel_exactly_two_hours_before_start"` (expect 1 test).
- Project checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

## Deployment notes
- No pre-deployment or post-deployment steps. No migrations or configuration.
- Tests run locally. The document records no pass results.

## Known limitations
- **No Seed scenario.** No seeder or seed test exists; the seeder was deferred from US-141 to US-140. The coverage matrix states this gap. Add the scenario once the seeder exists.
- Validation of 0 or more than 8 seats, booking a started showing, and creating a showing in the past are covered by `BookingCreateTests` and `ShowingTests` (API level), not by the scenario classes.
- The `dotnet test` commands and the `--list-tests` comparison were not executed by the developer or QA (dotnet unavailable at review time). They were checked by reading the code. Run them once in UAT.
- The hand-over stated 30 test files; the repository has 26. The document lists test methods, not file counts, so the difference does not affect its content. Confirm during UAT.
- Manual UI test plan is out of scope.
