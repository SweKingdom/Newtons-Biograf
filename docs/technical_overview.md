# Technical overview

## Solution structure

- `src/NewtonsBiograf.Shared/Enums.cs`: shared enums `Genre`, `AgeRating`, `SeatType`, `ShowingStatus`, `BookingStatus` (stored as strings).
- `src/NewtonsBiograf.Api/Data/`: `AppDbContext` (Identity-based), `ApplicationUser`, entities in `Entities.cs`, and the generic repository `IRepository<T>` / `Repository<T>`.
- `src/NewtonsBiograf.Api/Migrations/`: `InitialCreate` (hand-written, see Known gaps) and the model snapshot.
- `tests/NewtonsBiograf.Tests/DataTests.cs`: data layer tests using in-memory SQLite.

### Data model (US-117)
| Entity | Key points |
|---|---|
| Movie | Title (200, required); Genre; AgeRating; LengthMinutes (> 0 check); Description (2000) |
| Auditorium | Name (100, required, unique `IX_Auditorium_Name`); Rows; SeatsPerRow |
| Seat | AuditoriumId FK (cascade); RowNumber; SeatNumber; Type; unique (AuditoriumId, RowNumber, SeatNumber) |
| Showing | MovieId FK and AuditoriumId FK (restrict); StartTime and EndTime (UTC); Status (default Scheduled); index (AuditoriumId, StartTime) |
| Booking | Reference (8, required, unique); UserId FK to AspNetUsers (restrict); ShowingId FK (restrict); TotalPrice (decimal(10,2), stored as double); Status; CreatedAt; CancelledAt (nullable) |
| BookingSeat | BookingId FK (cascade); ShowingId; SeatId FK (restrict); Price; IsActive (default true); filtered unique (ShowingId, SeatId) where IsActive = 1 |

Enum values are stored as strings: Genre (Action, Comedy, Drama, Horror, SciFi, Animation, Documentary, Thriller); AgeRating (AllAges, Age7, Age11, Age15); SeatType (Standard, VIP); ShowingStatus (Scheduled, Cancelled); BookingStatus (Confirmed, Cancelled). All dates are UTC.

### Repository (US-117)
`IRepository<T> where T : class` is implemented once by `Repository<T>` over `AppDbContext`. It is registered open-generic (`AddScoped(typeof(IRepository<>), typeof(Repository<>))`). There is no unit of work and no per-entity repository. Services use `IRepository<T>` directly.

## Configuration

### JWT settings (US-119)

| Key | Default | Notes |
|---|---|---|
| `Jwt:Issuer` | `NewtonsBiograf` | appsettings |
| `Jwt:Audience` | `NewtonsBiograf.Client` | appsettings |
| `Jwt:Key` | none | Secret, at least 32 characters. Set with user secrets or `Jwt__Key`. The API fails at startup if it is missing or too short. `appsettings.Development.json` holds a marked dev-only key. |
| `Jwt:ExpiryMinutes` | 60 | Token lifetime. |

Identity password rules: minimum length 8; digit, uppercase, lowercase and non-alphanumeric requirements are off. Email is unique and used as the username.

## Configuration
| Key | File | Default | Purpose |
|---|---|---|---|
| `Cors:ClientOrigin` | `src/NewtonsBiograf.Api/appsettings.json` | `https://localhost:5001` | Origin allowed by the `Client` CORS policy |
| `ApiBaseUrl` | `src/NewtonsBiograf.Client/wwwroot/appsettings.json` | `https://localhost:7000/` | Base address of the API for the named `HttpClient` |

## API endpoints

### Authentication (US-119)

| Route | Access | Request | Success | Errors |
|---|---|---|---|---|
| `POST /api/auth/register` | Anonymous | `{Name, Email, Password}` | 201 `{Id, Name, Email}` | 400 ValidationProblemDetails (`errors` keyed by field). Duplicate email maps to `Email`; short password maps to `Password`. 500 if role `Customer` is missing (user is deleted). |
| `POST /api/auth/login` | Anonymous | `{Email, Password}` | 200 `{Token, ExpiresAt (UTC), Name, Role, Permissions[]}` | 401 ProblemDetails "Invalid email or password." |

Registration ignores role and permission fields. Every new user gets role `Customer`.

JWT claims: `sub` (user id), `email`, `name`, `role`, and one `permission` claim per stored claim. Signed with HS256.

Bearer validation for protected routes is not yet enabled (US-120).

## Known gaps

- JWT bearer validation and permission enforcement are not in place yet (US-120). Until then, the token is issued but no endpoint checks it.
- US-120 must set `RoleClaimType="role"` because tokens use the short claim name `role`.
- US-120 must read staff permissions from the database on each staff request, not from the token. Deactivated staff must be blocked immediately.
- No account lockout after repeated failed logins.
- No email confirmation or password reset.

## Client design system and layout (US-118)

- **Styles:** `wwwroot/css/app.css` holds all design tokens on `:root` (`--color-*`, `--font-body`, `--font-heading`, `--space-1..6`, radius, shadow). No external fonts. The only stylesheet linked from `wwwroot/index.html` is `app.css` (plus the scoped Blazor bundle). Bootstrap is not used. Mobile breakpoint: 640px.
- **Layout:** `Layout/MainLayout.razor` (+ `.razor.css`) provides header, top nav, `<main>@Body</main>` and footer. Below 640px the nav collapses behind a `<button aria-expanded>` toggle. No sidebar.
- **Components** (`Components/`): `AppButton` (Variant Primary/Secondary/Danger), `FormField` (Label, Id, Error with `role="alert"`), `AlertBox` (Kind Info/Success/Error), `Spinner` (`role="status"`), `Card` (Title).
- **Package references:** `NewtonsBiograf.Client.csproj` has an explicit `Microsoft.Extensions.Http` reference; the Client does not compile without it.
- **Build and test scripts:** `package.json` exposes `typecheck`, `test` and `build`, which wrap `dotnet build` and `dotnet test`. No lint script.
- **Tests:** `tests/NewtonsBiograf.Tests/DesignSystemTests.cs` checks the CSS and Razor source; components are not rendered.
- **Known gaps:** FormField error is not linked via `aria-describedby`; Home.razor handles only `HttpRequestException`; visual review at 375px and desktop is pending.

## Authorization policies (US-120)

- **Authentication:** JWT bearer, `MapInboundClaims=false`. Validation parameters come from `IOptions<JwtOptions>`; the signing key is validated on start.
- **Role claim:** `role` (`TokenService.RoleClaimType`).
- **Permission claim:** type `permission` (`Permissions.ClaimType`). Values: `ManageMovies`, `ManageShowings`, `ManageBookings`.
- **Roles:** `Admin`, `Staff`, `Customer`.
- **Policies:** `AdminOnly`, `CustomerOnly`, `ManageMovies`, `ManageShowings`, `ManageBookings`, registered by `AddAppPolicies`. Admin passes all permission policies; Staff needs the specific claim.
- **Errors:** 401 when unauthenticated; 403 when authenticated without role or permission. Both return `application/problem+json`; `OnChallenge` and `OnForbidden` prevent redirects.
- **Location:** `Api/Auth/Permissions.cs`; the client needs a shared copy later.

## Staff account management (US-121)

Admins create and list staff accounts through `AdminStaffController`. Both endpoints use the `AdminOnly` policy.

| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| POST | `/api/admin/staff` | Create a staff user (Name, Email, Password, Permissions[]) | 201, `Location: /api/admin/staff` | 400 ValidationProblemDetails (`Permissions`, `Password`, `Email`); 401; 403; 500 if role or claim assignment fails (user is deleted) |
| GET | `/api/admin/staff` | List users with role Staff, ordered by name, with permissions | 200, array of `StaffDto` | 401; 403 |

Behaviour:
- New users get role `Staff` and one `permission` claim per distinct permission. Permission names must match `Permissions.All` (`Api/Auth/Permissions.cs`) exactly, case-sensitively.
- Identity errors are mapped to the `Email` and `Password` fields.
- Passwords are never returned. Minimum length is 8 characters. Email is unique.

Contracts (`src/NewtonsBiograf.Shared/StaffDtos.cs`): `CreateStaffRequest` (Name: required, max 100 characters; Email; Password; Permissions[]) and `StaffDto`.

Known gaps: no delete or deactivate endpoint for staff. Permission updates are added by US-122.
