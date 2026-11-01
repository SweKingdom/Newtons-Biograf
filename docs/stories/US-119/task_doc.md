# US-119: Customer registration and login with JWT
**Status:** Ready for UAT · **Category:** security · **Points:** 5 · **Sprint:** not recorded

## Summary
Customers can now create an account with a name, email and password, and log in to receive a JWT. Every new account is a Customer; the registration form cannot grant any other role. Passwords are hashed by ASP.NET Core Identity.

## What was built
- `AuthController` (`src/NewtonsBiograf.Api/Controllers/AuthController.cs`): `POST /api/auth/register` and `POST /api/auth/login`, both `[AllowAnonymous]`. Uses `UserManager` and `SignInManager` directly.
- `TokenService` and `JwtOptions` (`src/NewtonsBiograf.Api/Auth/TokenService.cs`): creates HS256 tokens with `JwtSecurityTokenHandler`. Registered as a singleton.
- `Program.cs`: Identity core with roles (`AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>()`), password options, validated `Jwt` options, `TokenService` registration.
- `AuthDtos.cs` (`src/NewtonsBiograf.Shared/`): `RegisterRequest`, `LoginRequest`, `RegisterResponse`, `LoginResponse`.
- `appsettings.json` and `appsettings.Development.json`: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`; development-only signing key.
- `NewtonsBiograf.Api.csproj`: adds `System.IdentityModel.Tokens.Jwt`.
- `README.md`: configuration reference, including the `Jwt:*` keys.
- `tests/NewtonsBiograf.Tests/AuthTests.cs`: one test per AC, plus a missing-role case (500) and startup key validation.

## How it works
- **AC 1, Register:** a valid request creates the user and assigns role `Customer`. The response is 201 with `{Id, Name, Email}`. No token is returned; the user logs in next.
- **AC 2, Validation:** a password shorter than 8 characters returns 400 with `errors.Password`. A duplicate email returns 400 with `errors.Email`. No user is created.
- **AC 3, Login:** valid credentials return 200 with `{Token, ExpiresAt, Name, Role, Permissions[]}`. The JWT contains `sub`, `email`, `name`, one `role` claim and one `permission` claim per stored claim. Wrong password and unknown email both return 401 "Invalid email or password."
- **AC 4, No privileged registration:** the registration DTO has no role or permission fields, so any extra fields in the payload are ignored. The new user is always `Customer` with no permission claims.
- **Missing role:** if role `Customer` does not exist, or assignment fails, the new user is deleted and the request returns 500. The error is logged.

## How to test
Automated checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

Manual (run the API in Development, Swagger at `/swagger`; roles `Admin`, `Staff` and `Customer` must exist in the database):
- **AC 1:** POST `/api/auth/register` with a valid name, email and password. **Verify:** 201 `{id, name, email}`; the user has role `Customer`.
- **AC 2:** Register with a 7-character password, then twice with the same email. **Verify:** 400 with `errors.Password` and `errors.Email`; no new user row.
- **AC 3:** POST `/api/auth/login` with correct and incorrect credentials. **Verify:** 200 with a token (decode at jwt.io: `role`, `sub`, `email`, `name`); wrong password and unknown email both return 401 "Invalid email or password."
- **AC 4:** Register with extra fields `"Role":"Admin"` and `"Permissions":["ManageMovies"]`. **Verify:** the user is `Customer` with no claims.

## Deployment notes
**Pre-deployment**
- Set `Jwt__Key` (at least 32 characters) as an environment variable or user secret. The API does not start without it, or if it is shorter than 32 characters.
- Make sure roles `Admin`, `Staff` and `Customer` exist in the database. Seeding is outside this story.

**Post-deployment**
- Confirm the API starts and `POST /api/auth/login` responds.

**Configuration**

| Key | Default | Notes |
|---|---|---|
| `Jwt:Issuer` | `NewtonsBiograf` | appsettings |
| `Jwt:Audience` | `NewtonsBiograf.Client` | appsettings |
| `Jwt:Key` | none | Secret. Set via user secrets or `Jwt__Key`. A marked dev-only key exists in `appsettings.Development.json`. |
| `Jwt:ExpiryMinutes` | 60 | Product owner confirmed 60 minutes. |

## Known limitations
- JWT bearer validation is not wired into the request pipeline. Protected endpoints and permission enforcement are delivered in US-120.
- The role claim uses the short name `role`, not `ClaimTypes.Role`. US-120 must set `RoleClaimType="role"` when it adds bearer validation with `MapInboundClaims=false`.
- Staff permissions must be read from the database on each staff request, not from the token. Changes take effect immediately, and a deactivated staff account is blocked at once. This is required of US-120 (product owner answer).
- No account lockout is configured (not specified in the story).
- Email confirmation, password reset and external logins are out of scope.
- The manual Swagger checks were not run by QA. Verification was by static review and automated tests.
