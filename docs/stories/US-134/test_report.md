# US-134 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | All three exit 0 |
| AC 1 Map content | ✅ | ShowingSeatsController, SeatMapBuilder, SeatMapDto; test asserts exact ordered list with prices 140/200 |
| AC 2 Taken reflects bookings | ✅ | IsActive booking seats of this showing; tests for taken, cancel, other showing |
| AC 3 Not found | ✅ | null becomes NotFound() (ProblemDetails); test for unknown and cancelled |
| AC 4 Started showing | ✅ | IsBookable = Scheduled && start > now; boundary tested |

## Notes
- The code review found no failures: no secrets, anonymous access limited to a read-only endpoint, no user data in the DTO, and it fits the codebase.
- The TimeProvider deviation is documented in the ticket and is acceptable.
- The not-found test checks the problem+json media type only for the cancelled showing. The unknown-id path uses the same NotFound() result.

## Not tested
Nothing. The review was static plus the test run, with no live HTTP calls.
