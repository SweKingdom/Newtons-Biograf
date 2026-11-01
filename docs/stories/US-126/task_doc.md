# US-126: Auditorium API: create, view, edit with generated seats
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** Not recorded

## Summary
Admins can create auditoriums with a name, a number of rows and seats per row. The system generates the seat map and derives capacity from it. Admins can edit an auditorium's layout, but not when future showings already have bookings that would be affected. Anyone can view auditoriums.

## What was built
- `AuditoriumsController` (`src/NewtonsBiograf.Api/Controllers/AuditoriumsController.cs`): `GET /api/auditoriums` and `GET /api/auditoriums/{id}` (anonymous); `POST /api/auditoriums` and `PUT /api/auditoriums/{id}` (`AdminOnly`). Holds seat generation, layout edit, booking block (409) and the case-insensitive name check.
- `AuditoriumRequest` (validated input) and `AuditoriumDto` (`Id, Name, Rows, SeatsPerRow, Capacity, VipRows`) in `src/NewtonsBiograf.Shared/Dtos/AuditoriumDto.cs`.
- `AuditoriumTests` (`tests/NewtonsBiograf.Tests/AuditoriumTests.cs`): one scenario test per AC.

## How it works
- **AC 1 Create:** a valid POST saves the auditorium and its `Standard` seats in one save. The response is 201 with a Location header. `Capacity` = Rows × SeatsPerRow. Seats are Row 1..Rows, Number 1..SeatsPerRow.
- **AC 2 Unique name:** the name is trimmed and compared case-insensitively with existing names. A match returns 400 with the error on `Name`. Nothing is created. The same check applies on PUT, excluding the auditorium being edited. If a database unique-index conflict occurs during a race, it is also mapped to 400.
- **AC 3 Validation:** a blank or missing Name, a Name over 100 characters, Rows outside 1..26, SeatsPerRow outside 1..40, or a non-integer value returns 400 with per-field errors.
- **AC 4 View:** both GET endpoints are anonymous and return Id, Name, Rows, SeatsPerRow, Capacity and VipRows (row numbers sorted). An unknown id returns 404.
- **AC 5 Edit:** a PUT that changes only the name leaves seats untouched. A layout change keeps seats at unchanged positions (same Id), adds missing seats and removes extra ones. New seats take the type of their row; a new row gets `Standard`. Returns 204.
- **AC 6 Edit safety:** a layout change is rejected with 409 and the message "Layout cannot be changed because the auditorium has bookings." when either:
  - a future, non-cancelled showing in the auditorium has an active booking, or
  - a seat to be removed is referenced by any BookingSeat.
  Nothing is changed.
- **AC 7 Permissions:** Staff and Customer users get 403 on POST and PUT. Requests without a token get 401.

## How to test
**Checks:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`

- **AC 1 Create:** POST /api/auditoriums as Admin `{Name, Rows, SeatsPerRow}`. **Verify:** 201 + Location, Capacity = Rows × SeatsPerRow, Seat table has that many Standard rows.
- **AC 2 Unique name:** POST/PUT a name equal to another ignoring case/spaces. **Verify:** 400 with error on `Name`; nothing created.
- **AC 3 Validation:** blank/101-char Name, Rows 0/27, SeatsPerRow 0/41, `Rows: 1.5`. **Verify:** 400 with per-field errors.
- **AC 4 View:** anonymous GET list and by id. **Verify:** 200 with Id, Name, Rows, SeatsPerRow, Capacity, sorted VipRows; unknown id 404.
- **AC 5 Edit:** PUT name only, then grow/shrink the layout. **Verify:** 204; seats at unchanged positions keep their Id; extras removed; new seats take their row's type.
- **AC 6 Edit safety:** change layout with a future active booking, or removing a booked seat. **Verify:** 409 "Layout cannot be changed because the auditorium has bookings."; nothing changed.
- **AC 7 Permissions:** POST/PUT as Staff/Customer → 403; no token → 401.

## Deployment notes
- **Pre-deploy steps:** none.
- **Post-deploy steps:** none.
- **Migrations:** none. The schema is unchanged (unique Auditorium Name and unique Seat Auditorium+Row+Number come from US-117).
- **Configuration:** none.

## Known limitations
- **Anonymous GET returns more than the PO answer states.** The PO answer says the anonymous GET returns only id, name and capacity. The AC (and the delivery) also return Rows, SeatsPerRow and VipRows. Open question for the PO; the delivery follows the AC until it is confirmed.
- **409 message is fixed.** The PO answer asks for a message that names the affected showings. The AC fixes the text, so the message does not name the showings. Open question for the PO.
- **Race path untested.** The database unique-index race mapping to 400 has no test. The in-memory name check catches normal duplicates first. The mapping depends on the SQLite error message containing "Auditorium.Name".
- **Out of scope:** deleting auditoriums, changing VIP rows (separate story).
