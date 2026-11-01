# US-130: Edit and cancel showing (cancels all bookings)
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** not recorded

## Summary
Staff with ManageShowings can now edit a showing that has no bookings and has not started, cancel a showing (which cancels all its Confirmed bookings and frees their seats), and delete a showing that has no bookings. Showings with bookings or that have started cannot be edited; they can only be cancelled.

## What was built
- `ShowingsController`: `GET /api/showings` (staff list with `from`, `to`, optional `auditoriumId`), `GET /api/showings/{id}` (returns `ShowingDto` with `ActiveBookingCount`), `PUT /api/showings/{id}` (edit), `POST /api/showings/{id}/cancel` (cancel), `DELETE /api/showings/{id}` (delete).
- `ShowingService.CancelShowingAsync(id)`: cancels the showing and its Confirmed bookings in one database transaction; raises `BookingCancelled` per booking after commit.
- `BookingService.CancelInternalAsync(Booking, ...)`: sets the booking Status to Cancelled and `IsActive` to false; does not open a transaction or raise events.
- `BookingCancelled` record and `IBookingEvents` interface, with a default `LoggingBookingEvents` subscriber that logs each cancellation.
- `Program.cs`: registers `BookingService`, `IBookingEvents`, `AddProblemDetails()` and `UseExceptionHandler()` so unhandled errors return 500 problem+json.
- `ShowingTests.cs`: scenarios for AC 1 to AC 7, plus the list and GET endpoints.

## How it works
- **AC 1 Edit:** PUT on a future showing with no active bookings re-applies the slot rules (not in the past, no overlap with other showings; the showing itself is ignored). `EndTime` is recalculated. Returns 204.
- **AC 2 Cancel:** POST cancel sets `Status` to Cancelled and every Confirmed booking to Cancelled, with its BookingSeats set inactive, so the seats are bookable again. Returns 204. Calling it again on a cancelled showing returns 204 and raises no events.
- **AC 3 Delete:** DELETE with no bookings (any status) returns 204. With any booking it returns 409 "Showing has bookings and can only be cancelled." Unknown id returns 404.
- **AC 4 Atomic:** if any booking cancellation fails, the transaction rolls back, nothing changes, the response is 500 problem+json and no events are raised.
- **AC 5 Edit blocked:** PUT on a showing with active bookings or one that has started returns 409 and changes nothing.
- **AC 6 Events:** one `BookingCancelled` event per cancelled booking, raised after commit. If a subscriber throws after commit, the error is logged and the cancellation stands.
- **AC 7 Permissions:** no ManageShowings returns 403; anonymous returns 401; unknown id on PUT or cancel returns 404.

Times are stored and sent in UTC.

## How to test
Automated: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

Manual (Admin or Staff with ManageShowings; Swagger in Development):
- **AC1 Edit:** PUT `/api/showings/{id}` for a future showing with no Confirmed bookings. **Verify:** 204; GET `/api/showings/{id}` shows the new `EndTime`. A past start gives 400 on `StartTime`; an overlapping slot gives 409.
- **AC2 Cancel:** POST `/api/showings/{id}/cancel`. **Verify:** 204; showing Status is Cancelled; its Confirmed bookings are Cancelled with inactive BookingSeats. Repeat call returns 204.
- **AC3 Delete:** DELETE `/api/showings/{id}`. **Verify:** 204 without bookings; 409 "Showing has bookings and can only be cancelled." with any booking; 404 for unknown id.
- **AC4 Atomic:** covered by an automated test with a failing `BookingService`. **Verify:** 500 problem+json, showing and bookings unchanged, no events.
- **AC5 Edit blocked:** PUT on a showing with a Confirmed booking or one that has started. **Verify:** 409 with the "cannot be edited" message and nothing changed.
- **AC6 Events:** cancel a showing with two Confirmed bookings. **Verify:** the API log shows two "Booking ... cancelled" lines.
- **AC7 Permissions:** call the endpoints anonymously and as Customer. **Verify:** 401 and 403; unknown id with permission gives 404.

## Deployment notes
- No manual steps recorded.
- Requires the FK `Booking.ShowingId` to be Restrict (delivered in US-117).
- Configuration: none new.

## Known limitations
- Customer notification on cancellation is out of scope (email not included).
- Cancelled showings cannot be edited; a cancelled showing is replaced by creating a new one.
- Assumption confirmed by the product owner: a started showing cannot be edited; a showing with bookings can only be cancelled and recreated.
- Cancel has no time restriction; it is also allowed for started showings (staff correction).
