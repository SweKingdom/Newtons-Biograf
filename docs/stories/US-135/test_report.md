# US-135 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

`run_checks` passed in this run: typecheck, test and build all exited 0.

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Successful booking | ✅ | BookingsController, test Ac1 |
| AC 2 Price (480) | ✅ | test Ac2 |
| AC 3 Seat validity | ✅ | theory with 6 cases, nothing saved |
| AC 4 Showing closed or unknown | ✅ | 404 and 409 tests |
| AC 5 Double booking | ✅ | pre-check, interceptor rollback test |
| AC 6 Concurrency | ✅ | parallel test, 5 rounds |
| AC 7 Event | ✅ | once on success, none on failure |
| AC 8 Permissions | ✅ | 401/403/403/201 |
| AC 9 Reference | ✅ | alphabet, retry and exhaustion tests |
| AC 10 Rebook after cancel | ✅ | test Ac10 |

Verification was by static review of the delivered files plus the passing test run. No screenshots were captured.

## Notes
- The deviation (the publisher carries only `BookingCreated`) is acceptable.
- Reference retry exhaustion returns a 500, as the spec says other unique errors propagate.
