# US-134: Seat map API for a showing
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** —

## Summary
Customers can now request the seat map of a showing without logging in. The map lists every seat in the showing's auditorium with its type, price and whether it is taken. The map stays viewable for started showings, but the response marks them as not bookable.

## What was built
- `ShowingSeatsController` (`src/NewtonsBiograf.Api/Controllers/ShowingSeatsController.cs`): anonymous `GET api/showings/{id:int}/seats`.
- `SeatMapBuilder` (`src/NewtonsBiograf.Api/Services/SeatMapBuilder.cs`): static `BuildAsync(db, prices, showingId, TimeProvider, ct)`. Builds the map and is reusable by US-135.
- `SeatMapDto` and `SeatDto` (`src/NewtonsBiograf.Shared/Dtos/SeatMapDto.cs`): response contract.
- `SeatMapTests` (`tests/NewtonsBiograf.Tests/SeatMapTests.cs`): one scenario test per acceptance criterion.

## How it works
- **AC 1 (map content):** The endpoint needs no token. It returns `ShowingId`, `Start` and every seat of the showing's auditorium, ordered by Row, then Number. Each seat has `SeatId`, `Row`, `Number`, `Type` (`Standard` or `VIP`), `Price` (SEK, from pricing configuration) and `IsTaken`. The price is the whole-SEK decimal from `IPriceCalculator`, cast to int.
- **AC 2 (taken state):** A seat is taken (`IsTaken` = true) when it belongs to an active Confirmed booking for this showing. Cancelled bookings do not count, so the seat becomes free again. A booking for another showing does not affect this map.
- **AC 3 (not found):** An unknown showing id returns 404 ProblemDetails. A cancelled showing also returns 404.
- **AC 4 (started showing):** When `StartTime` is at or before the current time (`TimeProvider`) and the showing is not cancelled, the endpoint returns 200 with the map and `IsBookable` = false. Otherwise `IsBookable` = true.

## How to test
Checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`. Unit tests: `dotnet test --filter SeatMapTests`.

- **AC 1:** `GET /api/showings/{id}/seats` with no token. **Verify:** 200, every seat of the auditorium ordered by Row then Number, VIP seats Price 200, Standard 140.
- **AC 2:** Book a seat for the showing, fetch the map, cancel the booking and fetch again. **Verify:** `IsTaken` true, then false. A seat booked for another showing stays false.
- **AC 3:** Request an unknown id and the id of a cancelled showing. **Verify:** 404 `application/problem+json` for both.
- **AC 4:** Request a showing whose start time has passed. **Verify:** 200 with the map and `IsBookable` false. A future showing gives true.

## Deployment notes
- No migrations, no new configuration keys. The endpoint uses the existing pricing configuration (VIP rows and prices, US-127).
- No manual steps.

## Known limitations
- The `application/problem+json` media type is asserted by the test only for the cancelled-showing 404. The unknown-id 404 uses the same response path, but the test does not assert its media type.
- Real-time push updates of the seat map are out of scope. Clients must refetch the map to see changes.
- The spec mentioned an `IClock`. The codebase uses `TimeProvider` (registered as `TimeProvider.System`), so the implementation follows the codebase.
