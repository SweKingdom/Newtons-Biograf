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

## Staff permissions (US-122)

**Endpoint:** `PUT /api/admin/staff/{id}/permissions`, body `{ "Permissions": string[] }` (required, may be empty).

| Status | Cause |
|---|---|
| 204 | Permissions set (also when unchanged) |
| 400 | Unknown permission name (error key `Permissions`), or target is not in role Staff |
| 401/403 | Caller is not Admin |
| 404 | Unknown user id |

**Logic order:** validate names against `Permissions.All` → find user (404) → check role Staff (400) → compare current `permission` claims with the requested set → remove and add only the differences inside a `DbContext.Database.BeginTransactionAsync` transaction.

- Only claims of type `permission` are touched.
- Idempotent: same set returns 204 with no write.
- Tokens are not revoked. Changes apply when a new token is issued, which is governed by `Jwt:ExpiryMinutes` (default 60).
- No schema change or migration.

## Client authentication (US-123)

Blazor client authentication and role-aware UI.

| Component | Purpose |
|---|---|
| `Shared/Permissions.cs` | `Permissions` and `Roles` constants, shared by API and client. |
| `Client/Auth/JwtAuthStateProvider.cs` | `JwtParser.ParseClaims(token, now)` returns claims, or null for malformed, expired or exp-less tokens. `JwtAuthStateProvider` is a singleton, shared by the UI and `AuthMessageHandler`. Token stored in localStorage `nb_token`. |
| `Client/Auth/AuthMessageHandler.cs` | Adds `Authorization: Bearer` to API calls. 401 clears the token and redirects to login. 403 redirects to `/no-access`. Created by IHttpClientFactory in its own scope. |
| `Client/Auth/AuthRedirect.cs` | `SafeReturnUrl` accepts in-app addresses only. |
| `Client/Auth/ApiProblem.cs` | Maps `ValidationProblemDetails` to form field errors. |
| `Client/Components/AccessRedirect.razor` | Redirects anonymous users to login and signed-in users without rights to `/no-access`. |
| `Client/App.razor` | `AuthorizeRouteView` guards pages. |
| `Client/Layout/MainLayout.razor` | Role-based menu, user name and role, Log out. |

**Notes**
- The API remains the enforcement point. The UI only hides items and redirects.
- Staff-permission menu items are not added yet. Later stories add them with `AuthorizeView` and permission claims.
- Pages: `/login`, `/register`, `/no-access`, `/admin/staff`.
- Known gap: no component-level test for the Staff save flow.

## Movie API (US-124)

Base route: `/api/movies`. Implemented in `MoviesController`.

| Method | Route | Auth | Success | Errors |
|---|---|---|---|---|
| GET | `/api/movies` | Anonymous | 200 list | — |
| GET | `/api/movies/{id}` | Anonymous | 200 movie | 404 unknown id |
| POST | `/api/movies` | ManageMovies or Admin | 201 + Location | 400 validation, 401, 403 |
| PUT | `/api/movies/{id}` | ManageMovies or Admin | 204 | 400 validation, 401, 403, 404 |
| DELETE | `/api/movies/{id}` | ManageMovies or Admin | 204 | 401, 403, 404, 409 |

**Validation rules**

| Field | Rule |
|---|---|
| `Title` | Required, max 200 characters, trimmed |
| `Genre` | One of `Action`, `Comedy`, `Drama`, `Horror`, `SciFi`, `Animation`, `Documentary`, `Thriller` |
| `AgeRating` | One of `AllAges`, `Age7`, `Age11`, `Age15` |
| `LengthMinutes` | Whole number 1–400 |
| `Description` | Optional, max 2000 characters |

**Enum handling**
- `StrictEnumConverter` is registered globally. Enums accept names only. Numbers, numeric strings and unknown names fail.
- Conversion errors keyed by JSON path (for example `$.genre`) are re-keyed to the property name (`Genre`) with the message "{Field} has an invalid value." This is done by an `InvalidModelStateResponseFactory` in `Program.cs`.

**Delete rules**
- Future showings that are not cancelled: 409 "Movie cannot be deleted because it has future showings."
- Any other showing (past or cancelled): 409 "Movie cannot be deleted because it has showings." `Showing.MovieId` is Restrict, so there is no cascade.
- No showings: 204.

**Known gaps**
- Invalid enum values stop at the first conversion error.
- PUT with an invalid body and an unknown id returns 400, not 404.

## Movie management screens (US-125)

The Blazor client provides a staff screen over the movie API.

| Component | Path | Purpose |
|---|---|---|
| Movies page | `Pages/Staff/Movies.razor` | Route `/staff/movies`, `[Authorize(Policy = ManageMovies)]`. List, Add/Edit card, inline delete confirmation. |
| Movie form | `Pages/Staff/MovieForm.razor` | Parameters `Movie`, `Errors`, `Saving`, `OnSave`, `OnCancel`. Form uses `novalidate` so server messages are shown. |
| Movie API client | `Services/MovieApiClient.cs` | Typed client returning `MovieResult` (`Success`, `Problem`, `NotFound`, `Movies`). Enums sent by name via `StrictEnumConverterFactory`. |
| Problem mapping | `Auth/ApiProblem.cs` | Reads ProblemDetails `detail` (preferred) then `title`. `Field()` gives per-field errors, `General()` the message. |
| Navigation | `Layout/MainLayout.razor` | "Movies" entry inside `AuthorizeView Policy="ManageMovies"`. |

**Error handling.** HTTP 400 maps to field errors shown under each input. HTTP 409 (for example, movie with future showings) maps to a general message shown next to the delete panel; the row is kept. HTTP 404 and 403 and offline states are returned as `MovieResult` values and handled by the page.

**Configuration.** Client permission policies and the `MovieApiClient` registration are set in `Program.cs`. No new settings.

**Tests.** `tests/NewtonsBiograf.Tests/MovieClientTests.cs` covers client mapping. `DesignSystemTests.cs` asserts the US125 structure. There are no bUnit component tests yet (known gap).

## Auditorium API (US-126)

**Controller:** `AuditoriumsController` (no service layer, same approach as the movie API). Uses `IRepository<Auditorium>` and `AppDbContext` for seat queries.

**Endpoints**

| Route | Auth | Result |
|---|---|---|
| `GET /api/auditoriums` | anonymous | 200, list ordered by Name |
| `GET /api/auditoriums/{id}` | anonymous | 200, 404 |
| `POST /api/auditoriums` | `AdminOnly` | 201 + Location, 400, 401, 403 |
| `PUT /api/auditoriums/{id}` | `AdminOnly` | 204, 400, 404, 409 |

**Request (`AuditoriumRequest`):** Name (required, max 100), Rows (1–26), SeatsPerRow (1–40).

**Response (`AuditoriumDto`):** Id, Name, Rows, SeatsPerRow, Capacity, VipRows. Capacity is computed as Rows × SeatsPerRow and is not stored. VipRows are the row numbers whose seats are all VIP, from one grouped query.

**Rules**
- Name is trimmed and checked case-insensitively. A database unique-index conflict is mapped to 400.
- Create adds the auditorium and its Standard seats in one save.
- Edit keeps seats at unchanged positions, adds missing seats and removes extra seats. New seats take their row's type (Standard for new rows).
- Edit returns 409 with "Layout cannot be changed because the auditorium has bookings." when a future, non-cancelled showing in the auditorium has an active booking, or when a seat to be removed is referenced by a BookingSeat. Nothing is changed.
- Errors are returned as ProblemDetails.

**Data model:** no schema change in this story. Relies on the unique Auditorium Name and unique Seat (Auditorium, Row, Number) from US-117.

**Open points**
- The anonymous GET currently returns Rows, SeatsPerRow and VipRows. The PO answer mentions only Id, Name and Capacity. To be confirmed.
- The 409 message is fixed and does not name the affected showings, as the AC specifies. To be confirmed.

## Pricing and row seat types (US-127)

**Endpoints**

| Method | Route | Access | Notes |
|---|---|---|---|
| PUT | `/api/auditoriums/{id}/rows/{row}/type` | Admin | Body `{"Type":"Standard"｜"VIP"}`. 204 on success (idempotent). 404 unknown auditorium or row outside 1..Rows. 400 invalid or missing Type. 401 no token. 403 non-Admin. |
| GET | `/api/pricing` | Anonymous | Returns `{StandardSeat, VipSeat}` (SEK). |

**Configuration**

| Key | Default | Rule |
|---|---|---|
| `Pricing:StandardSeat` | 140 | Must be greater than 0 |
| `Pricing:VipSeat` | 200 | Must be greater than 0 |

Defaults live only in `appsettings.json`. The application fails at startup if a key is missing or not greater than 0.

**Components**
- `PricingOptions` (bound and validated on start in `Program.cs`).
- `IPriceCalculator` / `PriceCalculator`: singleton; reads values through `IOptionsMonitor`, so changes are picked up without code changes. `GetPrice(SeatType)` returns the price for a seat type.
- `RowTypeRequest`: nullable `Type` with `[Required]` and `[EnumDataType]`.

**Rules**
- Row type applies to all seats in a row; no per-seat override.
- Booking prices are snapshots taken through `IPriceCalculator` at booking time. Later price or row type changes do not alter existing bookings.
