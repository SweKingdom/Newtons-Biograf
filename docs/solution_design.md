# Solution Design — Newtons Biograf

Scope of this version: foundation (FT-106: US-116, US-117, US-118). Other features add sections later.

## Architecture overview
Blazor WebAssembly client (`NewtonsBiograf.Client`) ↔ .NET 8 Web API (`NewtonsBiograf.Api`, controllers, JWT bearer) ↔ EF Core + SQLite. Shared DTOs/enums in `NewtonsBiograf.Shared`. xUnit scenario tests in `NewtonsBiograf.Tests` using `WebApplicationFactory`. No external integrations.

## Module map
| Project | Content |
|---|---|
| `src/NewtonsBiograf.Api` | Program.cs, Controllers/, Data/ (AppDbContext, ApplicationUser, Entities, IRepository, Repository), Migrations/ |
| `src/NewtonsBiograf.Client` | Layout/, Components/ (AppButton, FormField, AlertBox, Spinner, Card), Pages/, wwwroot/css/app.css |
| `src/NewtonsBiograf.Shared` | DTOs, enums (Genre, AgeRating, SeatType, ShowingStatus, BookingStatus) |
| `tests/NewtonsBiograf.Tests` | xUnit tests; in-memory SQLite with migrations |

## Data model
Movie, Auditorium (unique Name), Seat (unique Auditorium+Row+Number), Showing (UTC times), Booking (unique 8-char Reference), BookingSeat (filtered unique ShowingId+SeatId WHERE IsActive=1), ApplicationUser (Identity + DisplayName). Enums stored as strings. Details in US-117.

## API inventory

| Route | Auth | Story |
|---|---|---|
| `GET /api/health` | anonymous | US-116 |
| `POST /api/auth/register` | anonymous; 201, 400 | US-119 |
| `POST /api/auth/login` | anonymous; 200 `{Token, ExpiresAt, Name, Role, Permissions[]}`, 401 | US-119 |
| `POST /api/admin/staff` | policy `AdminOnly`; 201, 400 | US-121 |
| `GET /api/admin/staff` | policy `AdminOnly` | US-121 |
| `PUT /api/admin/staff/{id}/permissions` | policy `AdminOnly`; 204, 400, 404 | US-122 |

Further routes defined by feature stories. All errors use ProblemDetails / ValidationProblemDetails (`application/problem+json`).

## Security model

- **Authentication:** ASP.NET Core Identity (`AddIdentityCore` + roles, EF stores); HS256 JWT from `Auth/TokenService.cs`. Claims: `sub`, `email`, `name`, role (`ClaimTypes.Role`, one of Admin/Staff/Customer), one `permission` claim per permission. `MapInboundClaims=false`. Default lifetime 60 min (`Jwt:ExpiryMinutes`, assumption).
- **Passwords:** length ≥ 8 only; unique email = username. Login returns the same 401 message for unknown user and wrong password.
- **Authorization (US-120):** policies `AdminOnly`, `CustomerOnly`, `ManageMovies`, `ManageShowings`, `ManageBookings`. Permission policies are `RequireAssertion`: Admin role OR claim `permission`=name. Constants in `Shared/Permissions.cs` (claim type, 3 values, `All`) used by Api and Client. No global fallback policy; each non-public controller has explicit `[Authorize]`.
- **Errors:** 401/403 return `application/problem+json` via `JwtBearerEvents.OnChallenge/OnForbidden`; no redirects.
- **Privilege:** registration always yields Customer and ignores role fields; only Admin creates Staff and edits permissions (US-121/122); permission edits only for users in role Staff.
- **Token revocation:** none; permission changes apply on next login/token expiry (accepted).
- **Client:** token in `localStorage` (`nb_token`), attached by `AuthMessageHandler`; UI hiding is cosmetic, API enforces. XSS exposure of localStorage accepted for this internal app.
- CORS restricted to `Cors:ClientOrigin`. Swagger only in Development.

## Configuration

`ConnectionStrings:Default`, `Cors:ClientOrigin` (Api); `ApiBaseUrl` (Client). `Jwt:Issuer` (`NewtonsBiograf`), `Jwt:Audience` (`NewtonsBiograf.Client`), `Jwt:Key` (secret, ≥32 chars, user-secrets/env `Jwt__Key`; startup fails if missing), `Jwt:ExpiryMinutes` (60). Prices in appsettings (later story).

## Integration inventory
None external.

## Key decisions

| Decision | Reason | Alternatives |
|---|---|---|
| Controllers rather than minimal APIs | Declarative authorization, familiar structure | Minimal APIs |
| Single generic `IRepository<T>`, no per-entity repos/unit of work | Meets generics requirement, lean | Specific repositories |
| Keep BookingSeat rows on cancel with `IsActive` flag + filtered unique index | Preserves history, frees seats, DB still prevents double booking | Delete rows on cancel |
| Enums stored as strings | Readable DB, safe against reorder | int |
| UTC datetimes everywhere | Avoid timezone bugs; client converts for display | local time |
| Custom CSS variables design, no Bootstrap | Requirement: not template-like | Bootstrap theme |
| Permission policies via `RequireAssertion` (Admin OR claim), no requirement/handler classes | One rule, fewer files | `IAuthorizationHandler` |
| Identity used directly in controllers (no StaffService/AuthService) | Thin logic, lean | Service layer |
| Permissions as Identity user claims (type `permission`) copied into the JWT | Standard Identity storage; admin editing needs no new tables | Custom permission table |
| No token revocation; permissions effective on next login | Not specified; short lifetime | Refresh tokens / security stamp check per request |
| Register returns 201 without token | Keeps login a single path | Auto-login |
| Permission constants in `Shared` | Client and API share one definition | Duplicate strings |

## Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| SQLite lacks native decimal / limited ALTER support in migrations | Med | Low | Whole-SEK prices with converter; keep migrations simple; recreate DB during development |
| Filtered index provider support | Low | High | Covered by a test with real SQLite |
| No checks/CI configured | High | Med | `dotnet build`/`dotnet test` run manually; CI out of scope |
| Committed SQLite file conflicts with migrations | Med | Med | Regenerate seed DB after each migration change |

## Movie management (FT-108)

Stories US-124 (API) and US-125 (UI).

| Route | Auth | Result |
|---|---|---|
| `GET /api/movies`, `GET /api/movies/{id}` | anonymous | 200, 404 |
| `POST /api/movies` | policy `ManageMovies` | 201, 400, 401, 403 |
| `PUT /api/movies/{id}` | policy `ManageMovies` | 204, 400, 404 |
| `DELETE /api/movies/{id}` | policy `ManageMovies` | 204, 404, 409 |

- `MoviesController` uses `IRepository<Movie>` directly; no `MovieService`. One `MovieRequest` for POST and PUT. DTOs in `Shared`.
- Delete is blocked (409) when the movie has a non-cancelled showing with `StartTime > UtcNow`. Cascade delete of showings is NOT used. The `Showing.MovieId` FK behaviour (US-117) must be confirmed: with `Restrict`, a movie that has only past or cancelled showings cannot be deleted, so the API returns 409 "Movie cannot be deleted because it has showings.".
- Enums are sent as strings (wire names), shown via display names ("Sci-Fi", "All ages").
- Client: `Pages/Staff/Movies.razor`, `MovieForm.razor`, `Services/MovieApiClient.cs`. It maps `ValidationProblemDetails` errors onto `FormField`s, and shows the 409 message in an AlertBox. The nav entry is under `AuthorizeView Policy="ManageMovies"`.
- Risk: the FK/delete rule for past showings is not in the requirements. Treat it as an assumption until confirmed by the product owner.

## Auditorium management (FT-109)

Stories US-126 (API), US-127 (VIP rows, prices), US-128 (UI).

| Route | Auth | Result |
|---|---|---|
| `GET /api/auditoriums`, `GET /api/auditoriums/{id}` | anonymous (assumption: public auditorium filter needs names) | 200, 404 |
| `POST /api/auditoriums` | `AdminOnly` | 201, 400, 401, 403 |
| `PUT /api/auditoriums/{id}` | `AdminOnly` | 204, 400, 404, 409 |
| `PUT /api/auditoriums/{id}/rows/{row}/type` | `AdminOnly` | 204, 400, 404 |
| `GET /api/pricing` | anonymous | 200 `{StandardSeat, VipSeat}` |

- `AuditoriumsController` uses `IRepository`/`AppDbContext` directly; no `AuditoriumService` (consistent with Movies). DTOs in `Shared/Dtos/AuditoriumDto.cs`.
- Capacity is computed (Rows × SeatsPerRow); `VipRows` computed from seats (rows whose seats are all VIP). Seats generated on create (all Standard). Layout edit keeps seats at unchanged positions (same Id), adds/removes the rest; new seats inherit their row's type.
- Layout change is rejected with 409 when a future non-cancelled showing has an active booking or a removed seat was ever booked (assumption, confirmed with PO pending). Bounds 1–26 rows, 1–40 seats per row (assumption). Name unique case-insensitive.
- Pricing: `Pricing:StandardSeat=140`, `Pricing:VipSeat=200` bound to `PricingOptions` (validated on start, >0). `IPriceCalculator.GetPrice(SeatType)` is the only price source; booking stores `TotalPrice` snapshot.
- Client: `Pages/Admin/Auditoriums.razor`, `AuditoriumForm.razor`, reusable `Components/SeatGrid.razor` (also for the booking seat map), `Services/AuditoriumApiClient.cs`; nav under `AuthorizeView Policy="AdminOnly"`.
- Decision: separate `GET /api/pricing` rather than embedding prices in auditorium DTOs · one source for UI and booking · embedding per DTO.
- Risk: Name case-insensitivity depends on SQLite collation; application-level check is authoritative. Seat removal vs BookingSeat FK (Restrict) — guarded by the 409 rule.

## Showing management (FT-110)

Stories US-129 (create + rules), US-130 (edit, cancel, delete), US-131 (UI).

| Route | Auth | Result |
|---|---|---|
| `POST /api/showings` | `ManageShowings` | 201, 400, 401, 403, 409 |
| `PUT /api/showings/{id}` | `ManageShowings` | 204, 400, 404, 409 |
| `POST /api/showings/{id}/cancel` | `ManageShowings` | 204, 404 |
| `DELETE /api/showings/{id}` | `ManageShowings` | 204, 404, 409 |
| `GET /api/showings`, `GET /api/showings/{id}` (staff) | `ManageShowings` | 200, 404 |
| `GET /api/showings/settings` | anonymous | 200 `{CleaningMinutes}` |

- `ShowingsController` + one `ShowingService` (deviation from the Movies/Auditoriums no-service pattern: slot validation is shared by create and edit, and cancel orchestrates `BookingService.CancelInternalAsync` in a transaction).
- Rules: `EndTime = Start + Movie.LengthMinutes + Showings:CleaningMinutes (15)`; `Start > UtcNow`; overlap = non-cancelled showing in same auditorium with `existing.Start < new.End AND new.Start < existing.End` (touching allowed); conflict → 409 naming movie, time range and auditorium (times in `Showings:DisplayTimeZone`, Europe/Stockholm).
- Cancel: single DB transaction; showing → Cancelled, each Confirmed booking cancelled via shared `CancelInternalAsync` (no own transaction/events); `BookingCancelled` events raised after commit; idempotent.
- Delete only with no bookings (FK Restrict); edit blocked (409) when showing has active bookings, has started, or is cancelled (assumption, PO question open).
- Config: `Showings:CleaningMinutes=15`, `Showings:DisplayTimeZone`. Index `(AuditoriumId, StartTime)` recommended.
- Client: `Pages/Staff/Showings.razor`, `ShowingForm.razor`, `Services/ShowingApiClient.cs`; nav under `AuthorizeView Policy="ManageShowings"`; end-time preview is cosmetic.
- Decision: overlap check at application level, no DB constraint · SQLite cannot express range exclusion · accept rare race (low-volume internal tool).
- Risks: concurrent creates may both pass the check (Low/Med); SQLite datetime comparison relies on UTC ISO storage (tested); `BookingService.CancelInternalAsync` contract must match across stories; time zone and edit-with-bookings pending PO answer.

## Public browsing and search (FT-111)

Stories US-132 (public API), US-133 (screens).

| Route | Auth | Result |
|---|---|---|
| `GET /api/public/showings?genre&date&auditoriumId&title` | anonymous | 200 `ShowingListItem[]`, 400 |
| `GET /api/public/genres` | anonymous | 200 `string[]` |
| `GET /api/public/auditoriums` | anonymous | 200 `[{Id, Name}]` |
| `GET /api/public/movies/{id}` | anonymous | 200 movie + upcoming showings, 404 |

- One `PublicController` (`[AllowAnonymous]`), queries inline on DbContext, no service. Staff `GET /api/showings` (FT-110) keeps `ManageShowings`.
- Window: non-cancelled, `now < Start <= now+14d`; filters ANDed; `date` is a local day in `Showings:DisplayTimeZone`; sorted by Start; `FreeSeats = seats − seats in Confirmed bookings`; cap 500 rows, no PII.
- Decision: separate `/api/public/*` namespace · avoids mixed auth on `/api/showings` and clash with FT-110 · alternative: AllowAnonymous on the staff route (rejected: changes FT-110 contract).
- Decision: title filter applied in memory over the ≤14-day set · SQLite LIKE is case-insensitive for ASCII only (å ä ö fail) · alternative: `EF.Functions.Like` (rejected), custom collation (more setup).
- Client: `Pages/Home.razor`, `Pages/MovieDetail.razor`, `Components/ShowingFilter.razor`, `Services/PublicApiClient.cs`. Book → `/showings/{id}/book`, anonymous redirected to login with `returnUrl`.
- Risks: needs injectable clock for deterministic tests (Low); login `returnUrl` support must exist (Med); display time zone assumption pending PO answer from FT-110.

## Seat booking (FT-112)

Stories US-134 (seat map), US-135 (create booking), US-136 (my bookings/read), US-137 (UI).

| Route | Auth | Result |
|---|---|---|
| `GET /api/showings/{id}/seats` | anonymous | 200 `SeatMapDto {ShowingId, Start, IsBookable, Seats[SeatId,Row,Number,Type,Price,IsTaken]}`, 404 |
| `POST /api/bookings` | `CustomerOnly` | 201 `BookingDto`, 400, 401, 403, 404, 409 (`SeatConflictDto {Message, SeatMap}` or showing closed) |
| `GET /api/bookings/mine?scope=upcoming\|history` | `CustomerOnly` | 200, 400 |
| `GET /api/bookings/{id}` | owner OR `ManageBookings` | 200, 403, 404 |
| `GET /api/bookings?showingId=&reference=` | `ManageBookings` | 200, 400 |

- Files: `ShowingSeatsController` (separate from the `ManageShowings`-protected `ShowingsController`), `BookingsController`, `Services/BookingService.cs`, `Services/SeatMapBuilder.cs`, `Events/BookingEvents.cs`, `Events/BookingLogSubscriber.cs`, `Shared/Dtos/SeatMapDto.cs`, `BookingDto.cs`.
- `BookingService` owns create/read and `CancelInternalAsync(Booking, now)`: sets Cancelled and `BookingSeat.IsActive=false`, no own transaction and no events. Callers (showing cancel, booking cancel) commit then raise `BookingCancelled`.
- Create: validate 1–8 distinct seats of the showing's auditorium; showing must be Scheduled and start in the future; pre-check taken; single `SaveChangesAsync` (atomic); on SQLite error 19 on the seat index, `ChangeTracker.Clear()` and return 409 with a fresh seat map; Reference collision retried up to 5 times. Reference: 8 chars from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, `RandomNumberGenerator`.
- Events: `delegate BookingEventHandler`, `IBookingEventPublisher` with `BookingCreated`/`BookingCancelled`; raised after commit; subscriber failures are logged and swallowed; one logging subscriber.
- Upcoming = Confirmed and showing start > now (`IClock`); history = the rest. Non-owner customer gets 403.
- Client: `Pages/Book.razor`, `Pages/MyBookings.razor`, `Services/BookingApiClient.cs`; seat map reuses and extends `Components/SeatGrid.razor` (no separate SeatMap component). `BookingLimits.MaxSeats=8` in Shared.
- Decision: cancelled bookings keep BookingSeat rows with `IsActive=false` (no seat snapshot on Booking) · history stays accurate and the filtered unique index frees seats · alternative: delete rows plus snapshot (rejected).
- Decision: seat map anonymous, in its own controller · map visible to visitors, booking needs login · alternative: authenticated only (rejected, blocks browsing).
- Decision: 409 body carries the refreshed seat map · one round trip for the client · alternative: client refetches.
- Risks: concurrency test needs a file-based SQLite DB (Med); `CancelInternalAsync` contract shared with FT-110 and the cancel-booking story; booking by staff on behalf of customers not specified (Customer only assumed); 403 vs 404 for another user's booking follows the story assumption; no client test project (manual UI checks).

## Seed data and scenario tests (FT-113)

Stories US-140 (seed + committed DB), US-141 (xUnit scenarios).

- Seed: `Data/DbSeeder.cs`, run at Development startup after `MigrateAsync`; inserts via the context, idempotent by natural keys, showings/bookings skipped if showings exist. Accounts `admin@`, `staff1@` (ManageMovies+ManageShowings), `staff2@` (ManageBookings), `customer1..3@newtons.local`; demo password from `Seed:Password`. `newtons.db` committed (`.gitignore` exception; WAL/SHM ignored).
- Tests: `tests/NewtonsBiograf.Tests` with `TestDb` (SQLite in-memory, kept-open connection; file-based variant for the parallel booking test), `ApiFactory` (WebApplicationFactory), `FakeClock : IClock`. Scenario files: Booking, Cancellation, ShowingOverlap, Access, Seed.
- Decision: tests use `IClock` rather than `TimeProvider` · one time abstraction across app and tests · alternative: adopt TimeProvider everywhere (rejected, already designed with IClock).
- Decision: real SQLite, not EF InMemory · filtered unique index must be enforced.
- Open (non-blocking): showings in the committed DB go stale after 14 days; assumed Development startup tops up missing future days.
- Risks: parallel test needs file DB (Med); cancel boundary (exactly 2h = allowed) and error codes must match the booking-cancel contract; no checks/CI configured, so tests run locally only.

## Cancellation and booking administration (FT-114)

Stories US-138 (cancel API), US-139 (UI). Note: FT-114's stories cover cancellation; seed data and scenario tests are designed under FT-113 (US-140/141).

| Route | Auth | Result |
|---|---|---|
| `POST /api/bookings/{id}/cancel` | `[Authorize]`; owner OR Admin OR `ManageBookings` (checked in `BookingService.CancelAsync`) | 204, 401, 403, 404, 409 |

- Order of checks: 404 → 403 → already cancelled 409 → time rule (owner without privilege only) 409. Privileged callers ignore the 2h rule, also after start.
- Time: `IClock` (not `TimeProvider`, per existing decision); limit from `Booking:CancellationLimitHours` (default 2) via `BookingOptions`; inclusive boundary (`now <= start − limit`).
- Cancel = `CancelInternalAsync` (seats `IsActive=false`), set `CancelledAt` (nullable column; migration `AddBookingCancelledAt` if US-135 did not add it), one commit, then `BookingCancelled` raised once.
- Too-late and already-cancelled both return 409 (story allowed 400/409; one code chosen).
- Client: `MyBookings.razor` (Cancel, confirm, disabled reason), new `Pages/Staff/Bookings.razor`, `BookingApiClient.CancelAsync/SearchAsync`, `BookingLimits.CancellationLimitHours` in Shared (UI mirror, server authoritative).
- Risks: client constant can drift from server config (Low); client clock skew only affects button state; staff showing selector may need an endpoint accessible to ManageBookings (assumed free-text fallback); no client tests.
