# US-128: Auditorium management screens
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** not recorded

## Summary
Admins can now list, create and edit auditoriums from the browser at `/admin/auditoriums`. The edit page shows read-only Standard and VIP prices and a seat grid where clicking a row label toggles that row between VIP and Standard. The pages and the nav entry are visible to admins only; the API still enforces access.

## What was built
- `Pages/Admin/Auditoriums.razor`: list page at `/admin/auditoriums` with name, rows, seats per row and capacity. Shows the spinner, the empty-state text and the load-error AlertBox.
- `Pages/Admin/AuditoriumForm.razor`: create at `/admin/auditoriums/new` and edit at `/admin/auditoriums/{Id:int}`. Plain `<form>` with `FormField`s. Shows read-only prices and the seat grid with VIP toggle.
- `Components/SeatGrid.razor`: reusable seat grid. Each row label carries the VIP/Standard text. The `Selected`, `Taken` and `OnSeatClick` parameters are reserved for the booking story and are not used here.
- `Services/AuditoriumApiClient.cs`: `List`, `Get`, `GetPrices`, `Create`, `Update`, `SetRowType`. Returns `AuditoriumResult`, like `MovieResult`.
- `Services/MovieApiClient.cs`: JSON options made `internal` so both clients share them.
- `Layout/MainLayout.razor`: Auditoriums entry in the Admin `AuthorizeView`.
- `wwwroot/css/app.css`: `--seat-standard` and `--seat-vip` variables, seat grid styles, `.btn` link reset.
- `Program.cs`: registers `AuditoriumApiClient`.
- Tests: `AuditoriumClientTests.cs` (new), `DesignSystemTests.cs` (US-128 page, CSS and nav checks), `MovieClientTests.cs` (stub handlers made internal for reuse).

## How it works
- **AC 1, List and form:** the list shows each auditorium with name, rows, seats per row and capacity. Create and edit accept Name, Rows and SeatsPerRow. On success the page returns to the list with the new data. This follows AC 1 and differs from the tech spec, which redirected to the edit page.
- **AC 2, Row preview:** the edit page renders a seat grid with labelled rows and numbered seats. Clicking a row label calls `SetRowType` at once and updates the colours. VIP uses gold with the text "VIP"; Standard uses the standard colour with the text "Standard". On failure the row reverts and an AlertBox shows the error.
- **AC 3, Prices:** the edit page shows the Standard and VIP prices from `GET /api/pricing`, read-only.
- **AC 4, Errors:** API ValidationProblemDetails are mapped to the matching field (Name, Rows, SeatsPerRow). A 409 is shown in an AlertBox. Typed values are kept after an error.
- **AC 5, Access:** the nav entry and pages use `[Authorize(Roles = Roles.Admin)]`. Non-admin and anonymous users do not see the entry and are denied on direct URL. The API `AdminOnly` policy remains the enforcement.
- **AC 6, States:** spinner while loading, empty-state text "No auditoriums yet. Add the first one." when the list is empty, error AlertBox on load failure.

## How to test
Automated:
- `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (or `dotnet test`).
- `AuditoriumClientTests` covers the API client. `DesignSystemTests` covers the page structure, CSS and nav.

Manual (run Api and Client, sign in as the seeded admin):
- **AC 1:** create "Sal 4" with 5 rows × 8 seats. **Verify:** returns to the list, which shows capacity 40. Edit it. **Verify:** the list shows the new data.
- **AC 2:** open Edit and click the row 2 label. **Verify:** the row turns VIP (gold, text "VIP") and persists after reload. Click again. **Verify:** it returns to Standard. Stop the API and click a row. **Verify:** the row reverts and an error AlertBox shows.
- **AC 3:** **Verify:** prices 140 and 200 are shown read-only on the form page.
- **AC 4:** enter a duplicate name. **Verify:** an error shows under Name. Enter Rows 0 or 27, and SeatsPerRow 0. **Verify:** errors show under their fields. Shrink an auditorium that has bookings. **Verify:** the 409 message shows in an AlertBox. **Verify:** typed values stay.
- **AC 5:** sign in as staff or customer, or stay anonymous. **Verify:** no "Auditoriums" nav entry. Open `/admin/auditoriums`. **Verify:** redirect to login or no-access.
- **AC 6:** **Verify:** a spinner shows while loading. Stop the API and open the list. **Verify:** an error AlertBox shows. Delete all auditoriums. **Verify:** "No auditoriums yet. Add the first one." shows.

## Deployment notes
- No database migration. No new configuration.
- Client and API must be deployed together, because the client uses the US-126 auditorium endpoints and the US-127 row type and pricing endpoints.

## Known limitations
- No automated UI tests (no bUnit). The row revert, return-to-list, spinner and error states are covered only by the manual checks above and by code review.
- Price editing is out of scope. Prices are read-only.
- The `SeatGrid` `Selected`, `Taken` and `OnSeatClick` parameters are unused until the booking story.
