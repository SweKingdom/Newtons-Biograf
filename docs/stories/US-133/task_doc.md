# US-133: Public browsing screens
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** not recorded

## Summary
Visitors can now browse upcoming showings on the home page without logging in. They can filter the list, open a movie to see its details and upcoming showings, and press Book to continue to the booking page (or to login first if they are anonymous).

## What was built
- `Client/Pages/Home.razor` (`/`): home page with showings of the next 14 days, grouped by day.
- `Client/Pages/MovieDetail.razor` (`/movies/{Id:int}`): movie detail page with its upcoming showings.
- `Client/Components/ShowingFilter.razor` (+ `.razor.css`): genre, date, auditorium and title inputs; emits `ShowingFilterModel` through an `EventCallback`.
- `Client/Components/ShowingTable.razor`: showing rows with the Book link and the "Sold out" state.
- `Client/Services/PublicApiClient.cs`: typed client (`GetShowingsAsync`, `GetGenresAsync`, `GetAuditoriumsAsync`, `GetMovieAsync`), `ShowingFilterModel`, `PublicResult<T>`, `DisplayTime`. Builds the query string with `Uri.EscapeDataString` and omits empty values.
- `Client/Auth/AuthRedirect.cs`: `BookUrl` (builds the booking or login URL) and `SafeReturnUrl`.
- `Client/Pages/Login.razor`: honours `returnUrl`.
- `Client/Program.cs`: registers `PublicApiClient` without the auth handler.
- `Api/Controllers/PublicController.cs`: `GET /api/public/movies/{id}` (anonymous). Returns `MovieDetailDto`; 404 for an unknown id.
- `Api/Controllers/ShowingsController.cs`: `DisplayTimeZone` returned from `/api/showings/settings`.
- `Shared/Dtos/ShowingDto.cs`: `ShowingListItem`, `MovieDetailDto`, `AuditoriumOption`, `ShowingSettingsDto.DisplayTimeZone`.
- Tests: `tests/NewtonsBiograf.Tests/PublicBrowsingTests.cs` (movie detail: 200 with own upcoming showings, 200 with empty list, 404) and `PublicClientTests.cs` (query building and escaping, 404 and error handling, grouping by day in the display zone, time-zone fallback, Book URLs for logged-in and anonymous visitors, page routes without login).

## How it works
- **AC 1 Browse:** An anonymous visit to `/` loads showings for the next 14 days. They are grouped under a heading with weekday and date in the display time zone. Each row shows movie, age rating, start time, auditorium and free seats.
- **AC 2 Filters:** Genre, date, auditorium and title combine. The list reloads on change; the title input is debounced by about 300 ms. "Clear filters" resets all inputs. No matches shows "No showings match your filters". A loading indicator and an error alert are shown while or when the request fails. The date input is limited to today through today + 13 days.
- **AC 3 Movie detail:** Clicking a movie title opens `/movies/{id}` with description, length, genre, age rating and upcoming showings. An unknown id shows "Movie not found".
- **AC 4 Book requires login:** Book on a logged-in visit goes to `/showings/{id}/book`. For an anonymous visit it goes to `/login?returnUrl=/showings/{id}/book`, and login returns the visitor there. Sold-out showings (0 free seats) show "Sold out" and a disabled Book button. Browsing never requires login.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`

**Manual (run API and Client, seed a few showings):**
- **AC 1:** Open `/` logged out. **Verify:** showings of the next 14 days are grouped under headings such as "Tuesday 5 November 2030" (Stockholm time); each row shows movie, age rating, start time, auditorium and free seats.
- **AC 2:** Change genre, date and auditorium; type in the title. **Verify:** the list reloads (title after a short pause) and the filters combine; "Clear filters" resets; no match shows "No showings match your filters"; a spinner shows while loading and an error alert when the API is stopped.
- **AC 3:** Click a movie title, then open `/movies/9999`. **Verify:** the detail shows description, length, genre, age rating and upcoming showings; the unknown id shows "Movie not found".
- **AC 4:** Click Book logged out, then logged in. **Verify:** logged out goes to `/login?returnUrl=%2Fshowings%2F{id}%2Fbook` and returns there after login; logged in goes straight to `/showings/{id}/book`. A sold-out showing shows "Sold out" and a disabled Book button.

## Deployment notes
- No database migrations.
- No new configuration. `Showings:DisplayTimeZone` is read from the settings response. Assumption: Stockholm (Europe/Stockholm) when not configured differently.
- Requires the US-132 public API and the login page with `returnUrl` support.

## Known limitations
- Trailers and images are out of scope.
- bUnit is not configured. Debounce, Clear filters, loading and error states and the Book click are verified manually only.
- The `returnUrl` round trip is covered by a test of `AuthRedirect.SafeReturnUrl`, not of `Login.razor` itself.
- The "no login needed" check for the home and movie pages verifies only that the pages have no authorization attribute.
- The booking page `/showings/{id}/book` is a separate story; the Book link leads to its route.
