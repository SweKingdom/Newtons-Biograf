# US-124: Movie API: create, view, edit, delete with validation
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** Not recorded

## Summary
Staff with the ManageMovies permission can now create, edit and delete movies through the API. Anyone can read the movie list and movie details without signing in. Invalid input is rejected with a clear error for each field, and nothing is saved.

## What was built
- `MoviesController` (`src/NewtonsBiograf.Api/Controllers/MoviesController.cs`): GET list and detail (anonymous); POST, PUT and DELETE under the ManageMovies policy.
- `MovieRequest` and `MovieDto` (`src/NewtonsBiograf.Shared/Dtos/MovieDtos.cs`): input and output contracts. The title is trimmed on save.
- `StrictEnumConverter` (`src/NewtonsBiograf.Shared/StrictEnumConverter.cs`): accepts enum values as names only. Numbers, numeric strings and unknown names are rejected.
- `Enums` (`src/NewtonsBiograf.Shared/Enums.cs`): `[Display]` names and the `ToDisplayName()` extension.
- `Program.cs`: registers the strict enum converter globally. Model errors keyed by JSON path (for example `$.genre`) are re-keyed to the property name (`Genre`) with the message "{Field} has an invalid value."
- `MovieTests` (`tests/NewtonsBiograf.Tests/MovieTests.cs`): scenario tests for each acceptance criterion.

## How it works
- **AC 1 Create:** A valid POST returns 201 with the saved movie. The stored values match the input, and the title is trimmed.
- **AC 2 Per-field errors:** An invalid body returns 400 as a problem document with errors keyed by field (`Title`, `LengthMinutes`, `Description`, `Genre`, `AgeRating`). Nothing is saved.
- **AC 3 Edit:** PUT with valid data returns 204. Invalid data returns 400. An unknown id returns 404.
- **AC 4 Delete blocked (future):** Deleting a movie that has a future showing that is not cancelled returns 409 with the detail "Movie cannot be deleted because it has future showings."
- **AC 5 Delete allowed:** Deleting a movie with no showings returns 204 and the movie is removed. A movie with only past or cancelled showings returns 409 "Movie cannot be deleted because it has showings." because the `Showing.MovieId` foreign key is Restrict. Booking history is kept.
- **AC 6 Permissions:** Anonymous write requests return 401. Customers and staff without ManageMovies return 403. Staff with ManageMovies and Admin can write. GET list and detail return 200 without a token.

## How to test
Run `npm run typecheck --silent`, `npm run test --silent` and `npm run build --silent`, or `dotnet test --filter MovieTests`.

- **AC 1:** POST /api/movies as Staff with ManageMovies. **Verify:** 201, Location header present, GET returns the same values, title trimmed.
- **AC 2:** POST with an empty title, length 0, a 2001-character description and genre "Western", "1" or 1. **Verify:** 400 problem+json with errors under `Title`, `LengthMinutes`, `Description` and `Genre`; no row saved.
- **AC 3:** PUT with valid data, invalid data and id 9999. **Verify:** 204, 400, 404.
- **AC 4:** Delete a movie with a future Scheduled showing. **Verify:** 409, detail "Movie cannot be deleted because it has future showings."
- **AC 5:** Delete a movie with no showings. **Verify:** 204, then GET returns 404. Delete a movie with only past or cancelled showings. **Verify:** 409 "Movie cannot be deleted because it has showings."
- **AC 6:** Call POST, PUT and DELETE with no token, as Customer, as Staff without the claim, as Staff with the claim, and as Admin. **Verify:** 401, 403, 403, success, success. Anonymous GET list and detail return 200.

## Deployment notes
- No manual pre- or post-deployment steps are recorded for this story.
- Depends on the data model (US-117) and authorization policies (US-120), both Release Ready.
- Client code that sends enum values must use names only (for example `Action`, `AllAges`, `SciFi`). The Blazor client (US-125) must use the same strict converter.

## Known limitations
- Enum wire names: `AllAges`, `Age7`, `Age11`, `Age15`, and `SciFi` for Sci-Fi.
- Delete of a movie with only past or cancelled showings is blocked (409). This is an assumption and the product owner should confirm it.
- On PUT, an invalid body with an unknown id returns 400 before 404, because automatic validation runs before the action. Authentication is still checked first.
- An invalid enum value stops at the first conversion error, so other field errors are not listed in the same response.
- Posters and images are out of scope. The movie UI is out of scope.
