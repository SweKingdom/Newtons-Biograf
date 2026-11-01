# US-129 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Create | ✅ | ShowingsController.Create, ShowingTests Ac1_* |
| AC 2 Past start | ✅ | ShowingService, Ac2_* (past, no offset, missing) |
| AC 3 Overlap | ✅ | overlap query + 409 detail, Ac3_* (incl. DST) |
| AC 4 Other auditorium | ✅ | Ac4 |
| AC 5 Back-to-back | ✅ | Ac5 theory (both boundaries) |
| AC 6 Cancelled | ✅ | Ac6 |
| AC 7 Unknown refs | ✅ | Ac7 theory |
| AC 8 Permissions | ✅ | Ac8 theory (401/403/201) |
| Checks | ✅ | typecheck, test, build exit 0 |

Evidence is static review of the files plus the passing test run; no screenshots were captured.

## Not tested
- Manual Swagger checks were not run; the scenario tests cover them against real in-memory SQLite.
