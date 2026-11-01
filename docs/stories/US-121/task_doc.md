# US-121: Admin creates staff accounts
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** not recorded

## Summary
Admins can now create staff accounts through the API. Each new account gets the Staff role and the permissions chosen at creation, which may be none. Admins can also list all staff with their permissions. Customers and staff cannot create accounts.

## What was built
- `AdminStaffController` (`src/NewtonsBiograf.Api/Controllers/AdminStaffController.cs`): `POST /api/admin/staff` creates a staff user and `GET /api/admin/staff` lists staff. Both use the `AdminOnly` policy and `UserManager<ApplicationUser>`.
- `CreateStaffRequest` and `StaffDto` (`src/NewtonsBiograf.Shared/StaffDtos.cs`): request and response contracts.
- `AdminStaffTests` (`tests/NewtonsBiograf.Tests/AdminStaffTests.cs`): covers AC 1 to AC 4, the 401/403 matrix, the rollback path and the empty list.

## How it works
- **AC 1, Create staff:** `POST /api/admin/staff` with Name, Email, Password and Permissions. On success the API returns 201 with `Location: /api/admin/staff`. The user gets role `Staff` and one `permission` claim per distinct permission. The password is never returned.
- **AC 2, Access control:** No token returns 401. A Customer or Staff token returns 403. Only Admin can call the endpoints.
- **AC 3, Validation:** An unknown permission name, a password shorter than 8 characters, or a duplicate email returns 400 with per-field messages under `Permissions`, `Password` or `Email`. No user is created. Permission names are matched case-sensitively. Permission names are checked against `Permissions.All` in `Api/Auth/Permissions.cs`.
- **Rollback:** If assigning the role or claims fails after the user is created, the user is deleted and the API returns 500.
- **AC 4, List staff:** `GET /api/admin/staff` returns only users with role Staff, ordered by name, each with its permissions. An empty list is returned as an empty array.

## How to test
Run `npm run typecheck --silent`, `npm run test --silent` and `npm run build --silent`.

- **AC 1:** As Admin, `POST /api/admin/staff` with `{Name, Email, Password, Permissions:["ManageMovies"]}`. **Verify:** 201, `Location: /api/admin/staff`, user has role Staff and one `permission` claim per distinct permission. No password in the response.
- **AC 2:** Call the endpoints with no token, a Customer token and a Staff token. **Verify:** 401, 403, 403. The same applies to GET. Admin gets 201 and 200.
- **AC 3:** Send an unknown permission, a 7-character password and a duplicate email (any letter case). **Verify:** 400 ValidationProblemDetails with keys `Permissions`, `Password` and `Email`. No user is created.
- **AC 4:** As Admin, `GET /api/admin/staff`. **Verify:** 200 with an array of Staff users only, ordered by name, each with its permissions.

## Deployment notes
- No pre-deployment or post-deployment steps are recorded for this story.
- No new configuration is recorded for this story.

## Known limitations
- Deleting or deactivating staff accounts is not specified and not built.
- Permission names are case-sensitive. `manageMovies` is rejected.
- Changing a staff member's permissions is handled by US-122, not this story.
