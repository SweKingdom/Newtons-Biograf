# US-139 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | all exit 0 |
| AC 1 Customer cancel | ✅ | MyBookings.razor confirm panel, CancelAsync then reload; tests |
| AC 2 Late cancel message | ✅ | disabled button and exact text; 409 message shown, list reloaded |
| AC 3 No button when not applicable | ✅ | BookingText.CanCancel; render tests |
| AC 4 Staff booking list | ✅ | Staff/Bookings.razor, policy ManageBookings, no 2h rule |
| AC 5 Error and empty states | ✅ | Spinner, "No bookings found", error alerts |

## Notes
- The confirm and reload flow and the empty state are checked through client tests and source assertions, not simulated clicks (no client test harness). This is a limited-proof area.
- API 403 for GET /api/bookings is covered by earlier stories, not re-tested here.

## Not tested
- Live browser behaviour (no browser available).
