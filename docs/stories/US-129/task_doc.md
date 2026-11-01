# US-129: Create showing with end-time, past-time and overlap rules
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** (not recorded)

## Summary
Staff with the ManageShowings permission can now schedule a showing of a movie in an auditorium. The system calculates the end time from the movie length plus a cleaning buffer, and rejects starts in the past, unknown references and clashes with other showings in the same auditorium.

## What was built
- `ShowingRequest`, `ShowingDto` (`src/NewtonsBiograf.Shared/Dtos/ShowingDto.cs`): request and response contracts.
- `ShowingOptions`, `ShowingService.ValidateSlotAsync` with result records `SlotOk`, `SlotInvalid`, `SlotConflict` (`src/NewtonsBiograf.Api/Services/ShowingService.cs`): end-time calculation, past-time, reference and overlap rules. `excludeId` lets US-130 reuse the check when editing.
- `ShowingsController`: `POST /api/showings` and `GET /api/showings/{id}` (target of the `Location` header), policy `ManageShowings`.
- `Program.cs`: `ShowingOptions` binding, validation on start (`CleaningMinutes >= 0`, `DisplayTimeZone` must be a known time-zone id), scoped `ShowingService`.
- `appsettings.json`: `Showings:CleaningMinutes = 15`, `Showings:DisplayTimeZone = Europe/Stockholm`.
- `tests/NewtonsBiograf.Tests/ShowingTests.cs`: scenario tests for AC 1–8 against in-memory SQLite, including a daylight-saving case.

## How it works
- **AC 1 Create:** A valid request is saved with status `Scheduled`. `EndTime` = `StartTime` + movie `LengthMinutes` + `Showings:CleaningMinutes`. The response is 201 with a `Location` header.
- **AC 2 Past start:** A `StartTime` at or before the current UTC time returns 400 with an error on `StartTime`. Nothing is saved. A missing `StartTime` or one without `Z`/offset (`DateTimeKind.Unspecified`) also returns 400 on `StartTime`. Offsets are converted to UTC.
- **AC 3 Overlap:** A non-cancelled showing in the same auditorium with `existing.Start < new.End` and `new.Start < existing.End` returns 409 (ProblemDetails). The detail names the clashing showing, e.g. "Overlaps with 'Dune' 18:00–20:55 in Salong 1", formatted in `Showings:DisplayTimeZone`. Nothing is saved.
- **AC 4 Other auditorium:** The same time in another auditorium returns 201.
- **AC 5 Back-to-back:** A start equal to an existing end (or an end equal to an existing start) returns 201.
- **AC 6 Cancelled:** Cancelled showings do not block; returns 201.
- **AC 7 Unknown references:** An unknown `MovieId` or `AuditoriumId` returns 400 with errors on `MovieId` / `AuditoriumId`. All validation errors (StartTime, MovieId, AuditoriumId) are returned together in one 400 response.
- **AC 8 Permissions:** No token → 401. Customer or Staff without ManageShowings → 403. Admin → allowed.

## How to test
Automated: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (or `dotnet test --filter ShowingTests`).

Manual (Swagger in Development, log in as Admin or Staff with ManageShowings and paste the token):
- **AC 1:** `POST /api/showings {"MovieId":1,"AuditoriumId":1,"StartTime":"2030-01-15T17:00:00Z"}`. **Verify:** 201, `Location` header, `status` Scheduled, `endTime` = start + movie length + 15 min.
- **AC 2:** Send a past `StartTime` or one without `Z`. **Verify:** 400 with an error on `StartTime`; nothing saved.
- **AC 3:** Repeat AC 1 with a start inside the first showing. **Verify:** 409 with detail such as "Overlaps with 'Dune' 18:00–20:55 in Salong 1" (Stockholm time).
- **AC 4:** Same start, other `AuditoriumId`. **Verify:** 201.
- **AC 5:** Start exactly at the first showing's end time. **Verify:** 201.
- **AC 6:** Set the overlapping showing's Status to Cancelled in the database and repeat. **Verify:** 201.
- **AC 7:** Use `MovieId` 9999 and/or `AuditoriumId` 9999. **Verify:** 400 with errors on `MovieId` / `AuditoriumId`.
- **AC 8:** Call without a token, as Customer, as Staff without ManageShowings, as Admin. **Verify:** 401, 403, 403, 201.

## Deployment notes
- No migration. The `(AuditoriumId, StartTime)` composite index already exists from US-117.
- Configuration to confirm per environment: `Showings:CleaningMinutes` (default 15) and `Showings:DisplayTimeZone` (default `Europe/Stockholm`). Startup fails if the values are invalid.

## Known limitations
- Editing and cancelling showings are out of scope (US-130).
- Recurring showings are out of scope.
- The 14-day scheduling window and the 2-hour cancellation limit are not part of this story.
- Manual Swagger checks were not run by QA; coverage is from the automated scenario tests.
