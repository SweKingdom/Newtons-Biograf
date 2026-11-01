# US-125: Movie management screens
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** Not recorded

## Summary
Staff can now maintain the movie programme from the web app: list movies, add, edit and delete them, without calling the API directly. Users with the ManageMovies or Admin permission see a Movies entry in the top menu and the Movies screen at `/staff/movies`.

## What was built
- `Pages/Staff/Movies.razor`: route `/staff/movies`, guarded by `[Authorize(Policy = ManageMovies)]`. Shows the movie table and an inline Add/Edit card, and handles the inline delete confirmation.
- `Pages/Staff/MovieForm.razor`: form component with parameters `Movie`, `Errors`, `Saving`, `OnSave` and `OnCancel`.
- `Services/MovieApiClient.cs`: typed client over the movie API, returning `MovieResult` (`Success`, `Problem`, `NotFound`, `Movies`). Enums are sent by name through `StrictEnumConverterFactory`.
- `Auth/ApiProblem.cs`: reads the ProblemDetails `detail` first (falls back to `title`), so the API's 409 message is shown to the user.
- `Layout/MainLayout.razor`: "Movies" menu entry inside `AuthorizeView Policy="ManageMovies"`.
- `Program.cs`: client permission policies and `MovieApiClient` registration.
- Tests: `MovieClientTests.cs` (success, 400 field errors, 409 detail, 404, 403, offline) and US125 structure assertions in `DesignSystemTests.cs`.

## How it works
**AC 1: List and form.** The Movies table shows Title, Genre, Age rating and Length, with Add, Edit and Delete per row. The form has Title, Genre (dropdown), Age rating (dropdown), Length, and Description. Both dropdowns start at "Select…" and offer all values.

**AC 2: Field errors.** The form uses `novalidate`, so the browser does not block submission. Server validation errors from the API are shown under the matching field. Nothing is saved when validation fails.

**AC 3: Delete blocked.** Delete opens an inline confirmation. If the API refuses the delete (movie has future showings, HTTP 409), the API message is displayed and the row stays in the list.

**Permissions.** Without ManageMovies or Admin, the menu entry is hidden and `/staff/movies` redirects to no-access.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

**Manual** (run API and client; log in as Staff with ManageMovies or Admin):
- **AC 1:** Open Movies in the menu. The table lists movies. Click Add movie; the form shows all five fields and both dropdowns start at "Select…". **Verify:** add a valid movie; it appears in the list with a success message. Edit and Delete work on a row.
- **AC 2:** Submit an empty title and length 0. **Verify:** messages appear under Title and Length (and under Genre and Age rating if unselected); nothing is added.
- **AC 3:** Delete a movie that has a future showing and confirm in the inline panel. **Verify:** the message "Movie cannot be deleted because it has future showings." is shown and the row stays.
- **Permissions:** Log in as Staff without ManageMovies. **Verify:** no Movies menu entry, and `/staff/movies` redirects to no-access.

## Deployment notes
- No database migration, configuration value or manual step is required.
- The client depends on the Movie API delivered in US-124 and the auth screens delivered in US-123.

## Known limitations
- Image upload is out of scope.
- There are no component (bUnit) tests for the Razor screens. Field error display and the row-kept-after-409 behaviour were verified by code review and by the client mapping tests only.
- No browser run or screenshots have been captured yet; the manual checks above are pending UAT.
- Deviation from the design: the Movies entry is in `MainLayout.razor` because `NavMenu.razor` does not exist. `MovieResult` exposes an `ApiProblem` instead of a separate errors dictionary and message.
