# US-139: Cancellation and booking administration screens
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** EP-105 / FT-114

## Summary
Customers can now cancel their own confirmed upcoming bookings from My bookings. Cancel is disabled with an explanation inside the 2-hour limit. Staff with the ManageBookings permission (or Admin) can search bookings by showing id or reference on the new page `/staff/bookings` and cancel any confirmed booking.

## What was built
- `BookingLimits.CancellationLimitHours = 2` (`src/NewtonsBiograf.Shared/Dtos/BookingDto.cs`): the cancellation cut-off in hours.
- `BookingApiClient.CancelAsync`: calls the cancel endpoint and returns a `BookingCancelResult`.
- `BookingApiClient.SearchAsync`: searches bookings by showing id and/or reference.
- `BookingText.CanCancel`, `IsTooLateForCustomer`, `LateCancelMessage`: decide when Cancel is shown and enabled, and hold the late-cancel message.
- `MyBookings.razor`: Cancel button, inline confirm panel, disabled state with explanation, notice and error messages.
- `Pages/Staff/Bookings.razor` (new, route `/staff/bookings`, policy `ManageBookings`): filters, results table, Cancel with confirm, loading, empty and error states.
- `MainLayout.razor`: "Bookings" navigation link shown for ManageBookings (Admin or permission).
- `tests/NewtonsBiograf.Tests/CancellationClientTests.cs`: tests per acceptance criterion.

## How it works
- **AC 1 (customer cancel):** On My bookings, a Confirmed upcoming booking shows Cancel. Clicking it opens an inline confirm panel. "Yes, cancel booking" calls the cancel endpoint, shows a success notice, and reloads the list. The booking moves from Upcoming to History with status Cancelled. "Keep booking" closes the panel without calling the API.
- **AC 2 (late cancel):** When the current time is later than the showing start minus 2 hours, Cancel is disabled and shows "Bookings can only be cancelled up to 2 hours before the showing starts." If the API still returns 409, its message is shown and the list is reloaded.
- **AC 3 (no button):** Cancelled and past bookings show no Cancel button.
- **AC 4 (staff list):** `/staff/bookings` lists bookings filtered by showing id and/or reference. Users without ManageBookings are redirected to the access-denied page, and the API returns 403. Cancel works on any Confirmed booking at any time, after confirm.
- **AC 5 (states):** A loading indicator shows while fetching. "No bookings found" shows when the result is empty. API errors are shown as plain messages. An empty search form shows a validation message.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (tests: `CancellationClientTests`).

**Manual checks**
- **AC 1:** Log in as a customer with a Confirmed booking more than 2h before the showing. Open My bookings, click Cancel, then "Yes, cancel booking". **Verify:** the booking leaves Upcoming and appears under History as Cancelled, and a success notice is shown. "Keep booking" closes the panel without calling the API.
- **AC 2:** Open a Confirmed booking starting in less than 2h. **Verify:** Cancel is disabled and the explanation text is visible. If the page was left open past the limit and Cancel is clicked, the API's 409 message is shown and the list reloads.
- **AC 3:** Open the History tab. **Verify:** Cancelled and past bookings have no Cancel button.
- **AC 4:** As Staff with ManageBookings (or Admin), open `/staff/bookings`, search by showing id and/or reference, and cancel a Confirmed booking, including one starting within 2h. **Verify:** after the confirm panel the booking shows Cancelled. As a customer, or Staff without the permission, `/staff/bookings` redirects to access-denied and `GET /api/bookings` returns 403.
- **AC 5:** **Verify:** a spinner shows while searching; an unmatched reference shows "No bookings found"; an empty search shows a plain validation message; with the API stopped, a search shows a plain error message.

## Deployment notes
- **Pre-deploy steps:** none.
- **Post-deploy steps:** none.
- **Configuration:** none. The 2-hour limit is a constant (`BookingLimits.CancellationLimitHours`), not a setting.
- **Migrations:** none.

## Known limitations
- The staff showing filter is a free-text showing id. The showings list endpoint requires `ManageShowings`, and the story asked for no new endpoint.
- The staff search needs at least one filter (the API returns 400 otherwise), so no list is shown until a search is made.
- `BookingDto.Start` is the showing start. The story calls it StartTime.
- After any cancel attempt the list is reloaded. The confirm dialog is an inline panel, not a modal (no JS interop).
- There is no client test project and no simulated UI events. The confirm and reload flow is verified through typed client tests and page source assertions. Server rules are covered by CancellationTests (US-138).
- The live browser was not tested in QA.
- Booking on behalf of customers is out of scope.
