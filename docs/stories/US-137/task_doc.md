# US-137: Seat selection and my bookings screens
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** not recorded

## Summary
Customers can pick seats on a visual seat map at `/showings/{id}/book`, see a running total in SEK, and receive a booking confirmation. Customers can also review their upcoming and past bookings at `/my-bookings`. Seat conflicts are handled by refreshing the map and dropping seats that are no longer free.

## What was built
- `SeatGrid.razor` (FT-109 component, extended): booking mode with `Seats`, `Selectable` and `SelectedIds`. Each seat is a `<button>` with `aria-pressed`, an `aria-label` and a text cue (★ VIP, ✕ taken, ✓ selected). Admin mode is unchanged.
- `Book.razor` (new): route `/showings/{ShowingId:int}/book`, `CustomerOnly` policy. Handles selection, the 8-seat limit, the running total, submitting, the 409 conflict, the confirmation, and the closed and not-found states.
- `MyBookings.razor` (new): route `/my-bookings` with Upcoming and History tabs, empty and error states.
- `BookingApiClient` (new): `GetSeatMapAsync`, `CreateAsync` (returns a result with `BookingDto`, `SeatConflictDto` or a problem message) and `GetMineAsync(scope)`. Uses `AuthMessageHandler`.
- `SeatSelection` and `BookingText` helpers: selection rules and display strings, kept testable outside the page.
- `BookingLimits.MaxSeats = 8` (Shared `Dtos/BookingDto.cs`): used by API validation in `BookingService` and by the client.
- `PolicyNames.CustomerOnly` (Shared `Permissions.cs`): mapped to `Policies.CustomerOnly` on the API and registered in the client `Program.cs`.
- `PublicApiClient.cs`: the GET helper is now reusable.
- `MainLayout.razor`: "My bookings" link, visible under `AuthorizeView Policy="CustomerOnly"`. There is no `NavMenu.razor` in this project.
- `wwwroot/css/app.css`: seat state variables and classes, tab styles, booking message styles.
- `tests/NewtonsBiograf.Tests/BookingClientTests.cs`: client, selection rules, and server-rendered page states.

## How it works
- **AC 1, Seat map:** The grid is shown by row and seat number with a legend for Free Standard, Free VIP, Taken and Selected. Clicking a free seat toggles it. Taken seats cannot be selected. A 9th selection is blocked with "You can select at most 8 seats." Each state has a colour and a text cue.
- **AC 2, Running total:** The selected count and the SEK total update on every toggle. The total is the sum of `SeatDto.Price` for the selected seats and is for display only; the server calculates the final amount. The Book button is disabled with 0 seats or while submitting.
- **AC 3, Confirmation:** A successful booking shows the reference, movie, time, seats and total, with a link to My bookings.
- **AC 4, Conflict:** On a 409, the message "One or more seats are no longer available" is shown. The map is replaced with the one returned by the server, and `selected.IntersectWith(freeIds)` keeps only seats that are still free.
- **AC 5, Auth and closed showing:** An anonymous user is redirected to login with `returnUrl`. A showing with `IsBookable` = false shows "This showing can no longer be booked" and no seat buttons. An unknown showing (404) shows a not-found message.
- **AC 6, My bookings:** The Upcoming and History tabs load their list by `scope` when the tab changes. Each row shows reference, movie, time, seats, total and status. Only the Customer role can open the page.
- Times are converted from UTC to local time for display, as on other pages.
- Accessibility: seats are keyboard operable with a focus outline. A live region (`role="status"`) announces the total and errors.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`. `BookingClientTests` covers the client, selection rules and page states.

**Manual (seeded data, customer account):**
- **AC 1:** Open `/showings/{id}/book`. Verify: grid by row and seat, legend, cues, `aria-label` such as "Row 3, seat 5, VIP, free", taken seats disabled, 9th click shows the limit message.
- **AC 2:** Toggle seats. Verify: count and SEK total update; Book is disabled at 0 seats and while submitting.
- **AC 3:** Select seats and press Book. Verify: confirmation with reference, movie, time, seats, total and My bookings link.
- **AC 4:** In two browsers, select the same seat, book in one, then the other. Verify: conflict message, refreshed map, taken seat deselected, other selections kept.
- **AC 5:** Open the URL logged out; open a started showing; open `/showings/99999/book`. Verify: login redirect with `returnUrl`; closed message with no seat buttons; not-found message.
- **AC 6:** Open `/my-bookings` as a customer and switch tabs; try as staff and anonymous. Verify: both lists with the required fields; empty state text; staff sent to no-access and anonymous users to login.

## Deployment notes
- No database migration, configuration value or manual step is introduced by this story.
- The `CustomerOnly` policy must be registered on both API and client (done in this story).
- Depends on US-133, US-135, US-136 and US-118 (all Release Ready).

## Known limitations
- Payment screen is out of scope.
- Automated tests do not cover clicks, the 409 UI flow or the confirmation rendering. The confirmation is checked by source text only.
- "Disabled while submitting" (AC 2) is not automatically tested.
- Browser behaviour (clicks, login redirect, two-browser conflict) is verified manually only; these checks must be run before release.
- Sprint is not recorded on the work item.
