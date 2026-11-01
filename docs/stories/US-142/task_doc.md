# US-142: README with run guide, accounts and three user scenarios
**Status:** Ready for UAT · **Category:** documentation · **Points:** 2 · **Sprint:** not recorded

## Summary
The repository README now describes the project, its technology, how to run the API and the client, the intended accounts, three user scenarios and a map of the code techniques used. It documents only what exists on this branch: the API does not migrate or seed at startup yet, so no login password is given.

## What was built
- `README.md` (repo root): ten specified sections plus an "API notes" appendix.
  - §4 states that nothing migrates or seeds at startup (checked against `Program.cs`).
  - §5 lists configuration keys as they appear in `appsettings.json`; `Seed:Password` is not listed.
  - §6 lists the intended accounts (email, role, permissions) with "to be set by US-140" in place of a password.
  - §7 gives three scenarios: customer books seats, staff schedules a showing (**Schedule showing**), admin creates a staff account (**Staff** > **Create staff account**).
  - §8 is a technique map (generics, delegates/events, LINQ, async/await) with file and class references. Events: only `BookingCreated` via `BookingEventHandler` is a delegate/event; cancellation uses `IBookingEvents`. Showing search/filter is in `PublicController.Showings`.
  - §9 links `docs/TESTING.md`.
- `docs/stories/US-142/test_report.md`: QA report.

## How it works
- **AC 1 (Content):** README covers project description, technology, running API and client, and the seeded accounts. The password is open (see Known limitations).
- **AC 2 (Scenarios):** three step-by-step scenarios in §7. They need the §6 accounts and can only be followed after US-140 is merged.
- **AC 3 (Technique map):** §8 table; each path, class and method was checked to exist.
- **AC 4 (Verified):** not closed. A fresh-clone run needs the seeded logins, which do not exist on this branch.

## How to test
- Run `npm run typecheck`, `npm run test`, `npm run build` (documentation-only change).
- **AC 1:** open `README.md`. **Verify:** sections 1–6 exist; config keys match `appsettings.json`; no `Seed:Password`; §4 says nothing migrates or seeds at startup; §6 gives no password.
- **AC 2:** read the three scenarios in §7 against the client. **Verify:** every page name, button and field label exists (`Log in`, `Find a showing`, `Book`, `Choose your seats`, `Booking confirmed`, `View my bookings`, `Schedule showing`, `Create staff account`). Run only after US-140.
- **AC 3:** open each path in the §8 table. **Verify:** every file and class/method named exists.
- **AC 4:** blocked until US-140 is merged. **Verify then:** `git clone` into an empty folder, follow §3–7, fill in the password and account list from `DbSeeder` and `appsettings.json`.

## Deployment notes
After US-140 is merged, update `README.md`:
- Remove the status note.
- Restore the startup migration and seeding text in §4.
- Add `Seed:Password` to §5.
- Fill in the password in §6 from `DbSeeder`.
- Check the database file name (`newtonsbiograf.db`) against `ConnectionStrings:Default`.
- Repeat the fresh-clone run of §3–7 (AC 4).

No runtime deployment is needed for this story.

## Known limitations
- The seeded account password is not documented; it does not exist on this branch (AC 1 open).
- AC 4 (fresh-clone run) is not verified.
- The §7 scenarios cannot be followed until US-140 is merged.
- The API reference beyond Swagger is out of scope.
