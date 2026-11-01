# US-138: Cancel booking: customer 2-hour rule and staff override
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** —

## Summary
Customers can cancel their own booking up to 2 hours before the showing starts. Staff with the ManageBookings permission, and Admins, can cancel any booking at any time. A cancelled booking keeps its history and its seats become free for rebooking.

## What was built
- `POST api/bookings/{id}/cancel` (`[Authorize]`) in `BookingsController`: maps service results to 204, 403, 404 and 409 (ProblemDetails).
- `BookingService.CancelAsync(int, ClaimsPrincipal)`: applies the checks, sets the booking to Cancelled, sets `CancelledAt`, deactivates the BookingSeat rows and raises `BookingCancelled`.
- Result records `BookingCancelledOk`, `BookingAlreadyCancelled` and `BookingTooLate`, returned instead of exceptions.
- `BookingOptions` (configuration section `Booking`), registered and validated in `Program.cs`.
- `Booking:CancellationLimitHours` = `2` in `appsettings.json`.
- New constructor dependencies of `BookingService`: `IBookingEvents`, `IOptions<BookingOptions>`, `ILogger<BookingService>`.
- Tests: `CancellationTests` (one scenario per AC, fake `TimeProvider`). `BookingCreateTests` and `ShowingTests` updated for the new constructor.

## How it works
- **AC 1 (customer in time):** the owner cancels before the limit. The API returns 204, the booking becomes Cancelled with `CancelledAt` set to the current time, and its seats are released so the seat map shows them as free.
- **AC 2 (too late):** the owner cancels after the limit. The API returns 409 with the detail "Bookings can only be cancelled up to 2 hours before the showing starts." Nothing changes and no event is raised.
- **AC 3 (boundary):** exactly 2 hours before the start is allowed (204). 1 hour 59 minutes before is rejected (409).
- **AC 4 (other's booking):** a customer cancelling another customer's booking gets 403 and nothing changes. An unknown id gets 404. A request without a token gets 401.
- **AC 5 (staff override):** Admin, or staff with ManageBookings, can cancel any booking at any time, including after the showing has started (204). Staff without ManageBookings get 403.
- **AC 6 (already cancelled):** cancelling a Cancelled booking returns 409 "Booking is already cancelled." for any role. `CancelledAt` and the event are not changed.
- **AC 7 (history):** the cancelled booking is returned by `GET api/bookings/mine?scope=history` with Status `Cancelled`, the same Reference and the same TotalPrice.
- **AC 8 (event):** `BookingCancelled` is raised exactly once per successful cancellation, after the commit. It is never raised on a rejection.

Check order: 404, then 403, then already cancelled (409), then the time rule (409).

## How to test
Checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`. Focused tests: `dotnet test --filter CancellationTests`.

Manual (API running, tokens from `POST /api/auth/login`):
- **AC 1:** owner cancels well before the showing. **Verify:** 204; `GET /api/showings/{showingId}/seats` shows the seats as free; they can be booked again.
- **AC 2:** owner cancels less than 2 hours before the start. **Verify:** 409 with the ProblemDetails text above; booking still Confirmed.
- **AC 3:** cancel exactly 2 hours before, and 1 hour 59 minutes before. **Verify:** 204 / 409.
- **AC 4:** another customer's token, an unknown id, no token. **Verify:** 403 / 404 / 401; booking unchanged.
- **AC 5:** Admin or staff with ManageBookings cancel after the showing has started. **Verify:** 204; staff without ManageBookings get 403.
- **AC 6:** cancel the same booking again (any role). **Verify:** 409 "Booking is already cancelled."; `CancelledAt` unchanged; no second "cancelled for showing" log line.
- **AC 7:** `GET api/bookings/mine?scope=history` as the owner. **Verify:** the booking has Status `Cancelled`, same Reference and TotalPrice.
- **AC 8:** **Verify:** the API log has exactly one "Booking … cancelled for showing …" line per successful cancellation and none on rejections.

## Deployment notes
- No database migration: `Booking.CancelledAt` already exists (US-135).
- Configuration: `Booking:CancellationLimitHours` in `appsettings.json` (default `2`). Invalid values fail startup because `BookingOptions` is validated.
- No pre-deploy or post-deploy manual steps.

## Known limitations
- No refunds: there is no payment in scope.
- The service sets `CancelledAt` from `TimeProvider`. `CancelInternalAsync` still uses `DateTime.UtcNow` internally, but `CancelAsync` overwrites it with the `TimeProvider` value. The internal path should be aligned in a later story.
- No automated test covers a subscriber that throws. The behaviour (logged, cancellation kept) is implemented but not yet covered by a test.
