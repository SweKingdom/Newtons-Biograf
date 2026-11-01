# US-143 — Test report (iteration 1)
**Verdict:** ✅ Approved (with notes) · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 — Scenario list | ✅ | `docs/TESTING.md` §3 and §4; the four scenario classes match the code |
| AC 2 — How to run | ✅ | `docs/TESTING.md` §2; README "Testing" section |
| Project checks | ✅ | `run_checks`: install, typecheck, test and build exited 0 |

## Checks
### AC 1: Scenario list — ✅ Works
**Steps:** Read `docs/TESTING.md` and compared the rows with `BookingScenarios`, `CancellationScenarios`, `AccessScenarios` and `ShowingOverlapScenarios`.
**Expected:** Every scenario has name, purpose, steps, expected result and a class.method mapping. **Found:** All 13 scenario methods have a row with the exact method name. The expected values match the asserts (480 total, 2 h boundary, 'Dune' 19:00–20:55 in Salong 1, 401/403/200). §4 maps each requirement area to tests. The missing Seed scenario is recorded as a gap, as the ticket specifies.

### AC 2: How to run — ✅ Works
**Expected:** `dotnet test` commands including filtering by scenario. **Found:** §2 has all, class, method, namespace, multi-class and per-area filters, plus `--list-tests`. By reading the code, the examples select the expected tests (3 for `BookingScenarios`, 1 for the method). The `~AuthTests` ambiguity is documented. README links to the doc.

## Not tested
- ⛔ The commands were not executed and the full `--list-tests` comparison was not done, because dotnet is unavailable to the tester. The developer also did not run them; names came from reading the code.
- Notes: the hand-over says "30 test files", but the repository has 26. The doc does not repeat this number. The doc has 17 numbered sections under §3, matching the ticket.
