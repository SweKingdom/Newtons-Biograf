# US-137 — Test report (iteration 1)
**Verdict:** ✅ Approved (with notes) · **Tester:** Cuélebre · **Date:** 2026-11-01

Method: static review of the delivered files against each AC, plus `run_checks` on the story branch. No screenshots were captured (no running app); evidence is file-based.

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Seat map | ✅ | `SeatGrid.razor` booking mode, `SeatSelection.Toggle`, `Ac1_*` tests |
| AC 2 Running total | ✅ | `Book.razor` summary and Book button state, `Ac2_*` test |
| AC 3 Confirmation | ✅ | `Book.razor` confirmation block, `Ac3_*` tests |
| AC 4 Conflict | ✅ | `BookingsController` 409 body, `BookAsync`, `KeepFree`, `Ac4_*` tests |
| AC 5 Auth / closed / 404 | ✅ | `App.razor` + `AccessRedirect`, `Book.razor` states, `Ac5_*` tests |
| AC 6 My bookings | ✅ | `MyBookings.razor`, `Ac6_*` tests |
| Checks (typecheck, test, build) | ✅ | all exit 0 |

## Checks
- **AC 1:** seats are buttons with `aria-label` "Row r, seat n, Standard|VIP, free|taken|selected", `aria-pressed`, cues ★ / ✕ / ✓, four-state legend, taken seats disabled. The 9th seat returns "You can select at most 8 seats." and is not added.
- **AC 2:** count and SEK total in a `role="status"` region. Book is disabled when 0 seats are selected or while submitting.
- **AC 3:** confirmation shows reference, movie, local time, seats, total and a link to `my-bookings`.
- **AC 4:** the API returns "One or more seats are no longer available" with the fresh map. The client replaces the map and intersects the selection with free seats. A 409 for a closed showing is shown as a plain error, not as a conflict.
- **AC 5:** anonymous users are redirected to login with `returnUrl`. Logged-in non-customers are sent to no-access. A non-bookable map shows "This showing can no longer be booked" with no buttons. A 404 shows a not-found message.
- **AC 6:** `role="tablist"`, `aria-selected`, scope loaded on tab change, all six columns, empty and error states, CustomerOnly.

## Notes (not failures)
- Click flow, the 409 UI flow and the confirmation rendering are not exercised by automated tests. The confirmation (AC 3) is asserted by matching source text, which is weak. The "disabled while submitting" state (AC 2) is not tested either. Manual checks from how-to-test remain advisable (two-browser conflict).
- The 409 user flow depends on the manual checks listed in how-to-test.

## Not tested
- Runtime behaviour in a browser (clicks, redirect, two-browser conflict): no client test harness or running app was available.
