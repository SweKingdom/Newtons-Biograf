# US-132: Public showings search and filter API
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** not recorded

## Summary
Anonymous visitors can now search and filter the showings starting in the next 14 days. The API also provides lists of genres and auditoriums to fill the filter dropdowns. The staff route `GET /api/showings` is unchanged and still requires `ManageShowings`.

## What was built
- `PublicController` (`src/NewtonsBiograf.Api/Controllers/PublicController.cs`, route `api/public`, `[AllowAnonymous]`):
  - `GET showings` returns `ShowingListItem[]` for the filters `genre`, `date`, `auditoriumId`, `title`.
  - `GET genres` returns the distinct genres as `string[]`, sorted.
  - `GET auditoriums` returns `AuditoriumOption[]` (`Id`, `Name`).
- `ShowingListItem` and `AuditoriumOption` records (`src/NewtonsBiograf.Shared/Dtos/ShowingDto.cs`).
- `TimeProvider.System` registered in `Program.cs`, so tests can fix the clock.
- `PublicBrowsingTests` (`tests/NewtonsBiograf.Tests/PublicBrowsingTests.cs`): one scenario test per AC.
- README: public endpoints documented.

## How it works
- **AC 1, default list:** Returns showings that are not cancelled and start in (now, now+14 days], sorted by start time, then Id.
- **AC 2, single filters:** `genre` matches exactly, ignoring case. `date` (`yyyy-MM-dd`) is read in `Showings:DisplayTimeZone` (Europe/Stockholm) and converted to a UTC window [00:00, next 00:00). `auditoriumId` matches the auditorium. `title` is trimmed, matches anywhere and ignores case. Matching runs in memory so that å, ä and ö are also case-insensitive.
- **AC 3, combined:** All supplied filters are applied with AND.
- **AC 4, exclusions:** Cancelled, past and beyond-14-day showings are never returned. A `date` outside the window returns `[]`.
- **AC 5, free seats:** `FreeSeats` = auditorium seat count − seats on Confirmed bookings. Cancelled bookings free their seats.
- **AC 6, invalid input:** A malformed `date`, a non-integer `auditoriumId` or a `title` over 100 characters returns 400 ProblemDetails. An unknown genre or auditorium returns `200 []`.
- **AC 7, staff route:** `GET /api/showings` returns 401 for anonymous callers and 403 without the `ManageShowings` claim.

## How to test
**Checks:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`. Unit tests: `PublicBrowsingTests` (fixed clock).

- **AC 1:** `GET /api/public/showings` without a token. **Verify:** 200, only non-cancelled showings in (now, now+14d], ascending by `start`.
- **AC 2:** `?genre=Horror`, `?date=2026-11-03`, `?auditoriumId=2`, `?title=dun`, `?title=ÅSA`. **Verify:** each returns only matching showings; title ignores case and matches anywhere, including å/ä/ö.
- **AC 3:** `?genre=Sci-Fi&title=dun&auditoriumId=1`. **Verify:** only showings meeting all three.
- **AC 4:** Seed a cancelled, a past and a day-15 showing, then repeat with matching filters. **Verify:** none appear; `?date=` outside the window returns `[]`.
- **AC 5:** Book seats (Confirmed) and cancel another booking. **Verify:** `freeSeats` = seats − Confirmed seats.
- **AC 6:** `?date=abc`, `?auditoriumId=x`, `?genre=Western`, `?auditoriumId=9999`. **Verify:** 400 problem+json for the first two, `[]` for the others.
- **AC 7:** `GET /api/showings?from=..&to=..` with no token and with a Customer token. **Verify:** 401 and 403.

## Deployment notes
- No database migration.
- Configuration: `Showings:DisplayTimeZone` must be set (value `Europe/Stockholm`). It is introduced by FT-110.
- No manual steps are required beyond deploying the build.

## Known limitations
- No paging. Results are capped at 500 rows (`Take(500)`).
- Title matching is done in memory after the database query. This is acceptable for the expected volume (under about 500 rows in the 14-day window).
- No full-text search.
