# US-123: Blazor login, registration and admin staff screens
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** not recorded

## Summary
Users can now register, log in and log out in the web app. The menu and pages reflect the user's role, and administrators can manage staff accounts and their permissions from a staff screen. The API stays the enforcement point for all access rules.

## What was built
- `NewtonsBiograf.Shared/Permissions.cs`: `Permissions` and `Roles` constants, moved from the API so the client can use `Roles.Admin`.
- `NewtonsBiograf.Api/Auth/Permissions.cs`: now only `Policies` and `AddAppPolicies`.
- `NewtonsBiograf.Client/Auth/JwtAuthStateProvider.cs`: `JwtParser.ParseClaims(token, now)` and `JwtAuthStateProvider` (token in localStorage key `nb_token`).
- `NewtonsBiograf.Client/Auth/AuthMessageHandler.cs`: attaches the Bearer token, handles 401 and 403.
- `NewtonsBiograf.Client/Auth/AuthRedirect.cs`: `SafeReturnUrl` restricts `returnUrl` to in-app addresses.
- `NewtonsBiograf.Client/Auth/ApiProblem.cs`: maps `ValidationProblemDetails` to form fields.
- `NewtonsBiograf.Client/Components/AccessRedirect.razor`: sends anonymous users to login and signed-in users without rights to `/no-access`.
- `NewtonsBiograf.Client/Pages/Login.razor`, `Register.razor`, `NoAccess.razor`, `Admin/Staff.razor`.
- `NewtonsBiograf.Client/Layout/MainLayout.razor`: role-based menu, user name and role, Log out.
- `NewtonsBiograf.Client/App.razor`: `AuthorizeRouteView`.
- `NewtonsBiograf.Client/Program.cs`, `_Imports.razor`, `NewtonsBiograf.Client.csproj` (Components.Authorization), `wwwroot/css/app.css`.
- Tests: `tests/NewtonsBiograf.Tests/ClientAuthTests.cs` (new), `DesignSystemTests.cs`, `NewtonsBiograf.Tests.csproj`.

## How it works
**AC 1: Register and login**
- Register and login forms show each API validation error under its field (via `ApiProblem`).
- A successful registration redirects to `/login` with a success notice.
- A successful login shows the user name and role in the header. The menu shows only items allowed for the role. A Customer sees no Staff link.
- A wrong password shows "Invalid email or password." in an alert box.

**AC 2: Admin staff screen**
- `/admin/staff` shows a staff table, a create form and the checkboxes ManageMovies, ManageShowings and ManageBookings.
- Permissions are saved on submit, both when creating and when editing ("Edit permissions").

**AC 3: Logout and guard**
- Log out clears the token from `nb_token`. The header returns to Log in / Register.
- Anonymous access to a protected page redirects to `/login?returnUrl=…`. After login, the user returns to that page. `returnUrl` is accepted only for in-app addresses.
- A signed-in user without the required role sees the "You don't have access to this page" page.

**HTTP behaviour**
- 401 from the API: the token is cleared and the user is sent to login.
- 403 from the API: the user is sent to `/no-access`.

## How to test
**Automated:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

**Manual** (run the Api with `Jwt__Key` set and a seeded Admin, then the Client):
- **AC 1:** Open `/register` and submit a 3-character password. Verify: "Password must be at least 8 characters." appears under the Password field. Register with valid data. Verify: redirect to `/login` with a success notice. Log in. Verify: the header shows your name and role, and no Staff link appears for a Customer. Log in with a wrong password. Verify: "Invalid email or password." appears in an alert box.
- **AC 2:** Log in as the admin and open `/admin/staff`. Verify: the staff table and create form with the three permission checkboxes. Create a staff account with one permission. Verify: it appears in the table. Click "Edit permissions", change the checkboxes and Save. Verify: the table shows the new permissions.
- **AC 3:** Click "Log out". Verify: the header shows Log in / Register and `localStorage.nb_token` is gone. Open `/admin/staff` anonymously. Verify: redirect to `/login?returnUrl=admin/staff`, and after login you return to the staff page. Log in as a Customer and open `/admin/staff`. Verify: "You don't have access to this page" is shown.

## Deployment notes
- Configure `Jwt__Key` for the Api (see the JWT settings in the technical overview).
- No database migration or manual step is required by this story.

## Known limitations
- Password reset is out of scope.
- No Staff menu items are shown to users with staff permissions yet, because no movie, showing or booking pages exist. Those stories add them using `AuthorizeView` and permission claims.
- No component-level test covers the Staff create and save flow. A manual browser check was not run in QA.
