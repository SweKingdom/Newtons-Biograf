# US-135: Create booking with database-enforced double-booking protection
**Status:** Ready for UAT · **Category:** development · **Points:** 8 · **Sprint:** n/a (not stated in source)

## Summary
Logged-in customers can book 1–8 seats for a scheduled, future showing through `POST /api/bookings`. Bookings are confirmed immediately with no payment. A unique database index guarantees that two customers can never hold the same seat, even under simultaneous requests.

## What was built
- `BookingsController` (`POST /api/bookings`, CustomerOnly): maps the service result to 201, 400, 404 or 409.
- `BookingService.CreateAsync`: validates seats, checks the showing, calculates the price, saves the booking in one `SaveChanges` call and raises `BookingCreated` after commit.
- `BookingService.NewReference()` (virtual, so tests can force collisions): generates the 8-character booking reference.
- `BookingService` result records for the create outcomes.
- `IBookingEventPublisher` (event `BookingCreated`) and `BookingEventPublisher` (singleton; catches subscriber errors so they never fail a request). Types `BookingEventArgs` and `BookingEventHandler` live in `Events/BookingEvents.cs`.
- `BookingLogSubscriber`: logs booking, user and showing ids only. Resolved at startup.
- Shared DTOs in `Shared/Dtos/BookingDto.cs`: `CreateBookingRequest`, `BookingSeatDto`, `BookingDto`, `SeatConflictDto`.
- Filtered unique index on booking seats (ShowingId, SeatId) for active (Confirmed) bookings, from the data model story.
- Tests: `BookingCreateTests` (one test per AC, including a deterministic rollback test and a parallel race on a file database). `SeatMapTests` and `ShowingTests` were adapted to the new `BookingService` constructor; no assertions changed.

## How it works
| AC | Behaviour |
|---|---|
| AC 1 Successful booking | Customer posts `ShowingId` and `SeatIds`. Response 201 with `Location: /api/bookings/{id}` and a `BookingDto` (Id, Reference, TotalPrice, Status `Confirmed`, seats, movie title, auditorium, start). |
| AC 2 Price | Total is the sum of seat prices from `IPriceCalculator`. Example: 2 Standard (140 each) + 1 VIP (200) = 480. |
| AC 3 Seat validity | 0 seats, more than 8, duplicate ids, or a seat outside the showing's auditorium returns 400 (ValidationProblemDetails). Nothing is saved. |
| AC 4 Showing closed | Unknown showing returns 404. A cancelled showing or one with `StartTime <= now` returns 409 "This showing can no longer be booked.". Nothing is saved. |
| AC 5 Double booking | If a seat is already in a Confirmed booking, the service returns 409 `SeatConflictDto` with the current seat map. No Booking or BookingSeat rows are saved. |
| AC 6 Concurrency | Simultaneous requests for the same seat: the unique index lets exactly one commit. The other gets 409 with the refreshed map. The whole booking rolls back. |
| AC 7 Event | `BookingCreated` (booking id, user id, showing id) is published once after commit on success. Never on failure. |
| AC 8 Permissions | No token returns 401. Staff or Admin token returns 403. |
| AC 9 Reference | 8 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`. Unique; on collision a new reference is generated, up to 5 retries. |
| AC 10 Rebooking | After a booking is cancelled through `CancelInternalAsync`, the same seats can be booked again (201). |

Error handling rules:
- A `DbUpdateException` on the `BookingSeat` unique index: rollback, 409 with refreshed map.
- A `Booking.Reference` unique violation: a new reference is generated and the save is retried.
- Any other unique violation: propagates (500).

## How to test
Checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (tests: `BookingCreateTests`).

- **AC 1:** log in as a Customer, `POST /api/bookings` with `{ShowingId, SeatIds:[..]}`. **Verify:** 201, `Location: /api/bookings/{id}`, body has Id, Reference, TotalPrice, Status `Confirmed`, Seats, MovieTitle, Auditorium, Start.
- **AC 2:** book 2 Standard + 1 VIP seat. **Verify:** `TotalPrice` is 480.
- **AC 3:** send 0 seats, 9 seats, a duplicate id, a seat of another auditorium. **Verify:** 400 with an error on `SeatIds`; no Booking row.
- **AC 4:** unknown showing id; cancelled or started showing. **Verify:** 404; 409 "This showing can no longer be booked.".
- **AC 5:** book the same seat twice. **Verify:** second call 409 `{Message, SeatMap}`; the seat is `IsTaken`; no extra Booking or BookingSeat rows.
- **AC 6:** fire two parallel requests for the same seat. **Verify:** one 201, one 409 with the refreshed map.
- **AC 7:** watch the log. **Verify:** one "Booking {id} created ..." line per successful booking, none on failures.
- **AC 8:** call without token, as Staff, as Admin. **Verify:** 401, 403, 403.
- **AC 9:** **Verify:** Reference is 8 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`.
- **AC 10:** cancel a booking (showing cancel) and book the same seats. **Verify:** 201.

## Deployment notes
- **Pre-deploy steps:** none recorded.
- **Post-deploy steps:** none recorded.
- **Configuration:** seat prices come from the existing pricing configuration (default Standard 140, VIP 200). No new settings.
- **Database:** the filtered unique index on booking seats must exist (data model story). Confirm it before deploying.
- **Startup:** `BookingLogSubscriber` is resolved at startup in `Program.cs`.

## Known limitations
- Booking is only for Customers. Staff or Admin booking on behalf of a customer is not specified and is not supported.
- No payment, tickets or confirmation emails (out of scope).
- `BookingCancelled` is not raised by the publisher. Cancel events stay on the existing `IBookingEvents` (US-130).
- No UI screen for booking is part of this story. The API is the only entry point.
