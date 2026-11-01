# US-138 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | all exit 0 |
| AC 1 Customer cancels in time | ✅ | BookingService.CancelAsync; test Ac1 |
| AC 2 Too late | ✅ | 409 + exact text; test Ac2 |
| AC 3 Boundary | ✅ | `now > start - limit`; two Ac3 tests |
| AC 4 Other's/unknown/no token | ✅ | 403/404/401 tests |
| AC 5 Staff override | ✅ | Admin + ManageBookings; 403 without |
| AC 6 Already cancelled | ✅ | 409, CancelledAt unchanged, one event |
| AC 7 History kept | ✅ | Ac7 test |
| AC 8 Event once after commit | ✅ | Ac8 tests |

Evidence is static review of BookingService.cs, BookingsController.cs, Program.cs, appsettings.json and CancellationTests.cs (no screenshots: API-only story).

## Notes (not failures)
- `CancelInternalAsync` sets `CancelledAt = DateTime.UtcNow`, which `CancelAsync` immediately overwrites with the TimeProvider value. Harmless, but the helper is shared with ShowingService.
- No test for a throwing subscriber (behaviour specified in tech spec, not an AC).
