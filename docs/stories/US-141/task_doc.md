# US-141: xUnit scenario tests for core business rules
**Status:** Ready for UAT · **Category:** testing · **Points:** 5 · **Sprint:** not recorded

## Summary
Adds a named, scenario-based xUnit suite that proves the core rules: booking, double booking, cancellation limits, showing overlap and data isolation. The tests run against real SQLite and a fake clock, so regressions in these rules are caught automatically. No end-user behaviour changes.

## What was built
- `TestDb` (`tests/NewtonsBiograf.Tests/Infrastructure/TestDb.cs`): real migrated SQLite, either in-memory (`InMemory()`) or temporary file (`File()`, deleted on dispose). Seeds 2 auditoriums, a movie and 2 customers. Helpers: `AddShowing`, `AddBooking`, `Counts`.
- `FakeClock : TimeProvider` (same file): controllable time for cancellation and overlap rules.
- `ApiFactory : WebApplicationFactory<Program>` (`Infrastructure/ApiFactory.cs`): test database, fake clock, recorded `BookingCreated` and `BookingCancelled` events, `With<TService,TResult>`, `Principal`, and a signed-in `Client`.
- `RecordingEvents`: captures raised domain events for assertions.
- `BookingScenarios` (AC 1, AC 2), `CancellationScenarios` (AC 3), `ShowingOverlapScenarios` (AC 4), `AccessScenarios` (AC 5).

## How it works
- **AC 1 Booking seats:** a 3-seat booking (2 Standard, 1 VIP) is Confirmed, total equals the sum of seat prices (480), the reference is 8 characters, 3 active seat rows exist, and one `BookingCreated` is raised.
- **AC 2 Double booking:** a second booking of a taken seat is rejected and leaves row counts unchanged. A parallel attempt on the same seat (file-based SQLite) yields exactly one success and one conflict.
- **AC 3 Cancellation limits:** customer cancel at 3h and at exactly 2h succeeds (inclusive boundary); at 1h and 1h59 it is rejected. Staff with ManageBookings and Admin cancel at 1h successfully. Staff without ManageBookings get 403.
- **AC 4 Overlap:** an overlapping showing is rejected and the conflicting showing ("Dune", 19:00–20:55, Salong 1) is named. A start equal to the previous end, including the 15-minute gap, is accepted. A showing in the other auditorium is accepted.
- **AC 5 Other customer's booking:** reading another customer's booking returns 403. A request without a token returns 401. `mine` returns only the caller's own bookings.
- All scenario classes share `[Collection("Scenarios")]`, so they do not run in parallel.

## How to test
Run `dotnet test tests/NewtonsBiograf.Tests` (or `npm run test`), then `npm run typecheck` and `npm run build`. Filter with `dotnet test --filter FullyQualifiedName~Scenarios`.

- **AC 1:** run `BookingScenarios.Customer_books_three_seats_gets_confirmed_booking_with_total_and_reference`. **Verify:** passes; total 480, 8-char reference, 3 active seat rows, one BookingCreated.
- **AC 2:** run `Booking_a_taken_seat_is_rejected_and_leaves_no_partial_rows` and `Two_parallel_bookings_of_the_same_seat_give_one_success_and_one_conflict`. **Verify:** both pass; counts unchanged after the conflict; one success and one conflict.
- **AC 3:** run `CancellationScenarios`. **Verify:** 3h and 2h succeed; 1h and 1h59 are rejected; ManageBookings and Admin succeed at 1h; no ManageBookings gives 403.
- **AC 4:** run `ShowingOverlapScenarios`. **Verify:** overlap rejected naming Dune 19:00–20:55 in Salong 1; back-to-back accepted; other auditorium accepted.
- **AC 5:** run `AccessScenarios`. **Verify:** other customer 403; no token 401; customer listing 403; staff with ManageBookings 200; `mine` returns only own bookings.
- **AC 6:** run `npm run test`. **Verify:** whole suite green.

## Deployment notes
- No configuration, migration or deployment step. Test-only change.
- Test project and package references already existed; no csproj change.

## Known limitations
- `SeedScenarios` was not created because no seeder exists. It is deferred to US-140.
- Time abstraction is `TimeProvider` (registered in Program.cs), not `IClock` as the original spec said.
- Showing starts are fixed at 2040-03-10 18:00 UTC because `ShowingService.ValidateSlotAsync` still compares against `DateTime.UtcNow`. Tests may need updating once that service uses the injected `TimeProvider`.
- No automated checks (CI) are configured in the project yet; the suite runs locally only.
- UI tests are out of scope.
