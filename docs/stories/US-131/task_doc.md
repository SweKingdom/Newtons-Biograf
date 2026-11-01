# US-131: Showing management screens
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** Not set

## Summary
Staff with the ManageShowings permission (or Admin) can now schedule, edit, cancel and delete showings from the web app. The screens sit on top of the showing API delivered in US-129 and US-130, and show the API's conflict messages directly on the form.

## What was built
- `Showings.razor`: list page at `/staff/showings` with a date range (default today + 14 days), an auditorium filter, a status badge, and Edit / Cancel / Delete actions with inline confirmations.
- `ShowingForm.razor`: create and edit form (movie, auditorium, date, time) with a computed end-time preview, field errors under inputs, and an overlap `AlertBox` above the form.
- `ShowingApiClient.cs`: typed client for List, Get, GetSettings, Create, Update, Cancel and Delete. Maps 400 responses to field errors and 409 responses to a message. Includes `ShowingTimes` helpers for local/UTC conversion.
- `MainLayout.razor`: Showings navigation entry inside `AuthorizeView Policy="ManageShowings"`.
- `Program.cs`: registers `ShowingApiClient`.
- `ShowingsController.cs`: anonymous `GET /api/showings/settings` returning `CleaningMinutes`.
- `ShowingDto.cs`: `ShowingSettingsDto`.
- `ShowingClientTests.cs`: automated client tests.

## How it works
- **AC 1 Schedule:** the form shows "Ends about …" once a movie and a start time are chosen. The end time is start + movie length + cleaning minutes (15, from the settings endpoint). Saving creates the showing and refreshes the list.
- **AC 2 Conflict:** a 400 response puts the `StartTime` message under the Date field. A 409 response (overlap) shows the API detail in a red `AlertBox` above the form. Entered values are kept.
- **AC 3 Cancel:** Cancel opens an inline confirmation that states the number of active bookings (`ActiveBookingCount`) that will be cancelled. Confirming calls the cancel endpoint and the row shows Cancelled.
- **AC 4 Edit / Delete:** Edit is disabled with a tooltip when the showing has bookings or has started. Delete is shown only when active bookings = 0. A 409 from the API is shown in a red alert.
- **AC 5 Access:** the nav entry and the page require ManageShowings or Admin. Other users see a not-authorized message.
- The API is the authority. The end-time preview is informational only.
- Times are UTC in the API and database, shown and entered in browser-local time, and sent as ISO UTC with `Z`.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent` (includes `ShowingClientTests`).

**Manual** (log in as Admin or as Staff with ManageShowings, open Showings):
- **AC 1 Schedule:** choose movie, auditorium, date and time. **Verify:** the "Ends about …" preview equals start + film length + 15 min; Save adds the row to the list.
- **AC 2 Conflict:** schedule an overlapping showing, or a start in the past. **Verify:** a past start shows an error under Date; the overlap message appears in a red alert above the form; entered values stay.
- **AC 3 Cancel:** click Cancel on a scheduled showing with bookings. **Verify:** the confirmation names the number of active bookings; confirming makes the row show Cancelled.
- **AC 4 Edit / Delete:** **Verify:** Edit is disabled with a tooltip for showings with bookings or already started; Delete appears only when active bookings = 0; a 409 from the API is shown in a red alert.
- **AC 5 Access:** log in as Customer or as Staff without ManageShowings. **Verify:** no Showings nav entry; `/staff/showings` shows the not-authorized message.

## Deployment notes
- No database migrations.
- No configuration to set. The cleaning minutes value comes from the existing `ShowingOptions` setting (US-129).

## Known limitations
- No automated UI component tests (bUnit is not configured). Screens are verified manually in UAT.
- Calendar drag-and-drop is out of scope.
- The not-authorized message relies on the existing `AccessRedirect` component, which was not re-reviewed in this story.
