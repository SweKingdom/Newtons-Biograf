# US-141 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Booking seats | ✅ | BookingScenarios.cs |
| AC 2 Double booking + parallel | ✅ | BookingScenarios.cs (file SQLite, 5 rounds) |
| AC 3 Cancellation limits | ✅ | CancellationScenarios.cs |
| AC 4 Overlap | ✅ | ShowingOverlapScenarios.cs |
| AC 5 Access | ✅ | AccessScenarios.cs |
| AC 6 Suite passes | ✅ | run_checks: install, typecheck, test, build exit 0 |

## Notes
- Static review: tests use real SQLite and a fake TimeProvider and assert real outcomes (counts, statuses, status codes, events).
- Deviations (TimeProvider instead of IClock, no SeedScenarios until US-140) are documented and acceptable.
