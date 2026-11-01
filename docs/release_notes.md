# Release 1.0.0 — 2026-11-01

## Highlights
- Cinema staff can manage movies, auditoriums (with VIP rows and seat prices) and showings, with end-time, past-time and overlap rules enforced.
- Customers can register, log in, browse showings, pick seats and book tickets; double-booking is blocked by the database.
- Customers can cancel their own bookings up to 2 hours before a showing; staff can cancel with override.
- Role-based access (Admin, Staff, Customer) with 401/403 responses and admin management of staff accounts and permissions.
- Public browsing, seat map, my bookings and staff booking administration screens are in place.

## Delivered

| Code | Title | Category | Points |
|---|---|---|---|
| US-116 | Create solution structure and remove boilerplate | devops | 3 |
| US-117 | Data model, generic repository and initial migration | development | 5 |
| US-118 | Custom Blazor design system and layout | development | 5 |
| US-119 | Customer registration and login with JWT | security | 5 |
| US-120 | Role and permission authorization policies (401/403) | security | 3 |
| US-121 | Admin creates staff accounts | development | 3 |
| US-122 | Admin changes and removes staff permissions | development | 2 |
| US-123 | Blazor login, registration and admin staff screens | development | 5 |
| US-124 | Movie API: create, view, edit, delete with validation | development | 3 |
| US-125 | Movie management screens | development | 3 |
| US-126 | Auditorium API: create, view, edit with generated seats | development | 5 |
| US-127 | VIP rows and configurable seat prices | development | 3 |
| US-128 | Auditorium management screens | development | 3 |
| US-129 | Create showing with end-time, past-time and overlap rules | development | 5 |
| US-130 | Edit and cancel showing (cancels all bookings) | development | 5 |
| US-131 | Showing management screens | development | 3 |
| US-132 | Public showings search and filter API | development | 3 |
| US-133 | Public browsing screens | development | 3 |
| US-134 | Seat map API for a showing | development | 3 |
| US-135 | Create booking with database-enforced double-booking protection | development | 8 |
| US-136 | My bookings: upcoming and history API | development | 2 |
| US-137 | Seat selection and my bookings screens | development | 5 |
| US-138 | Cancel booking: customer 2-hour rule and staff override | development | 5 |
| US-139 | Cancellation and booking administration screens | development | 3 |
| US-141 | xUnit scenario tests for core business rules | testing | 5 |
| US-142 | README with run guide, accounts and three user scenarios | documentation | 2 |
| US-143 | Test scenario documentation | documentation | 2 |

Total: 27 stories, 102 points.

## Checks
Run on the base branch with `run_checks` in this release run:

| Check | Command | Result |
|---|---|---|
| install | `npm install --no-audit --no-fund` | ✅ exit 0 |
| typecheck | `npm run typecheck --silent` | ✅ exit 0 |
| test | `npm run test --silent` | ✅ exit 0 |
| build | `npm run build --silent` | ✅ exit 0 |

## Smoke test results

| Code | Result | Note |
|---|---|---|
| US-116 | ✅ | Solution structure on base; checks green |
| US-117 | ✅ | Data model and hand-written migration on base; caveat on migration (see Known issues) |
| US-118 | ✅ | Design system and layout on base; DesignSystemTests green |
| US-119 | ✅ | AuthController and TokenService on base; requires `Jwt__Key` |
| US-120 | ✅ | Policies on base; 401/403 matrix tests green |
| US-121 | ✅ | AdminStaffController on base; tests green |
| US-122 | ✅ | PUT permissions on base; tests green |
| US-123 | ✅ | Login, register and staff screens on base; ClientAuthTests green |
| US-124 | ✅ | MoviesController and DTOs on base; MovieTests green |
| US-125 | ✅ | Movie screens on base; MovieClientTests green |
| US-126 | ✅ | AuditoriumsController on base; AuditoriumTests green |
| US-127 | ✅ | Pricing on base; PricingTests green |
| US-128 | ✅ | Auditorium screens and SeatGrid on base; AuditoriumClientTests green |
| US-129 | ✅ | ShowingService and controller on base; ShowingTests green |
| US-130 | ✅ | Showing edit/cancel/delete on base; US-130 scenarios green |
| US-131 | ✅ | Showing screens on base; ShowingClientTests green |
| US-132 | ✅ | PublicController on base; PublicBrowsingTests green |
| US-133 | ✅ | Public pages on base; PublicClientTests green |
| US-134 | ✅ | Seat map API on base; SeatMapTests green |
| US-135 | ✅ | BookingsController and BookingService on base; BookingCreateTests green |
| US-136 | ✅ | My bookings API on base; BookingReadTests green |
| US-137 | ✅ | Book and MyBookings pages on base; BookingClientTests green |
| US-138 | ✅ | Cancel endpoint and 2-hour rule on base; CancellationTests green |
| US-139 | ✅ | Cancellation and staff bookings screens on base; CancellationClientTests green |
| US-141 | ✅ | Test infrastructure and scenarios on base; green |
| US-142 | ✅ | README on base (documentation story) |
| US-143 | ✅ | docs/TESTING.md on base and linked from README |

Smoke checks were made from the base branch: each story's solution components were confirmed present and its test suite passed in `run_checks`. The API and UI were not started against a live database in this run.

## Known issues
- **US-117 migration is hand-written.** `dotnet ef` could not run in the build environment. Post-deployment step: run `dotnet ef migrations add Check --project src/NewtonsBiograf.Api` once. The result should be empty; remove it with `dotnet ef migrations remove`. If it is not empty, regenerate `InitialCreate`.
- **US-142 README is a status-note version.** After US-140 is merged, remove the status note, restore the startup/seeding text, add `Seed:Password`, and fill in the account passwords. Account passwords are not set by this release. US-140 is not in this release.
- **Seeding is outside this release.** Roles `Admin`, `Staff` and `Customer` must exist in the database (US-119). Startup auto-migration and seeding come with US-140.
- No linked bugs were opened.

## Rollback plan
- **Previous tag:** none. This is the first tagged release, so there is no earlier version to return to. Rollback means stopping the deployment and restoring the previous environment state.
- **Migrations:** `InitialCreate` (US-117) has no down step recorded. To roll back, drop the SQLite database file and recreate it, or restore it from backup. Only do this when no real bookings exist.
- **Data to restore:** the SQLite database file named in `ConnectionStrings:Default`, from backup taken before deployment.
- **Configuration:** `Jwt__Key` and `Pricing`/`Showings`/`Booking` settings in `appsettings.json` must match the release before the API starts.
- **Decision:** the release manager decides on rollback after a failed post-release check. The product owner confirms any data restore.