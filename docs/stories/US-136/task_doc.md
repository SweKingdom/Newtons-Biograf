# US-136: My bookings: upcoming and history API
**Status:** Ready for UAT · **Category:** development · **Points:** 2 · **Sprint:** not recorded

## Summary
Customers can now list their own bookings as upcoming or history, and read a single booking by id. Staff with the ManageBookings permission (or Admin) can read any booking and search bookings by showing or reference. Customers never see another customer's booking data.

## What was built
- `BookingsController` (`src/NewtonsBiograf.Api/Controllers/BookingsController.cs`): `GET api/bookings/mine?scope=upcoming|history` (CustomerOnly), `GET api/bookings/{id:int}` (owner or ManageBookings), `GET api/bookings?showingId=&reference=` (ManageBookings).
- `BookingService` (`src/NewtonsBiograf.Api/Services/BookingService.cs`): `GetMineAsync`, `GetByIdAsync` (returns Ok / NotFound / Forbidden), `SearchAsync`, and a shared `ToDto` projection used by all reads. Create now fills `CreatedAt`.
- `BookingDto` (`src/NewtonsBiograf.Shared/Dtos/BookingDto.cs`): new `BookingScope` enum (`Upcoming`, `History`); new properties `CreatedAt` and `CancelledAt?`.
- `BookingReadTests` (`tests/NewtonsBiograf.Tests/BookingReadTests.cs`): scenario tests for AC 1–7.

## How it works
- **AC 1 Upcoming:** `scope=upcoming` returns the caller's bookings where status is Confirmed and the showing start is after the current UTC time, earliest first.
- **AC 2 History:** `scope=history` returns the caller's remaining bookings (cancelled bookings and bookings whose showing has started), latest first, with `Status`.
- **AC 3 Scope validation:** a missing or unknown `scope` returns 400. `scope` is parsed case-insensitively. Upcoming and history together contain each booking exactly once.
- **AC 4 Own detail:** `GET api/bookings/{id}` for an own booking returns 200 with the BookingDto (movie title, auditorium, UTC start, seats with row, number and type, reference, total price, status).
- **AC 5 Other customer:** another customer's booking id returns 403 with no booking data. An unknown id returns 404.
- **AC 6 Staff:** ManageBookings or Admin can read any booking by id and search with `showingId` and/or `reference` (at least one required, otherwise 400; reference matched exactly, case-insensitively, capped at 200 results). Staff without the permission get 403 on the list route. Anonymous requests get 401 on all routes.
- **AC 7 Isolation:** `mine` only ever contains the caller's bookings, even when two customers booked the same showing.

## How to test
**Checks:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (or `dotnet test --filter BookingReadTests`).

- **AC 1:** As a Customer, `GET /api/bookings/mine?scope=upcoming` lists only own Confirmed bookings on future showings, earliest first.
- **AC 2:** `scope=history` lists own cancelled bookings and bookings on started showings, with `status`, latest first.
- **AC 3:** No or unknown `scope` returns 400; `scope` in upper case works; upcoming plus history lists each booking once.
- **AC 4:** `GET /api/bookings/{id}` for an own booking returns 200 with movie, auditorium, UTC start, seats, reference, price and status.
- **AC 5:** Another customer's booking id returns 403 without data; an unknown id returns 404.
- **AC 6:** Staff with ManageBookings or Admin can read any booking and call `GET /api/bookings?showingId=&reference=`; no filter returns 400; staff without permission get 403; no token returns 401.
- **AC 7:** Two customers on the same showing each see only their own bookings in `mine`.

## Deployment notes
- No migrations and no configuration changes. The API routes are added to the existing bookings controller.
- No manual steps.

## Known limitations
- The DTO property for the auditorium is named `Auditorium`, not `AuditoriumName` as in the specification. Consumers must use `Auditorium`.
- The scenario tests use the real system clock, not a fake clock. Upcoming/history boundaries are therefore verified against data relative to the current time, not a fixed date.
- Non-owners receive 403 for existing bookings and 404 for unknown ids, which reveals whether a booking id exists. This follows the story assumption and should be reviewed if privacy requirements change.
- No export or print (out of scope).
