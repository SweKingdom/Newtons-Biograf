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
