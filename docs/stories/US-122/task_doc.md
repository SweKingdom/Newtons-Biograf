# US-122: Admin changes and removes staff permissions
**Status:** Ready for UAT · **Category:** development · **Points:** 2 · **Sprint:** — (not recorded in item)

## Summary
Admins can now replace the full permission set of a Staff account, or remove all of its permissions, in one call. Changes take effect at the user's next login, because tokens issued earlier keep their old permissions until they expire.

## What was built
- `PUT /api/admin/staff/{id}/permissions` action in `AdminStaffController`. Inherits the `AdminOnly` policy. Uses `AppDbContext` to wrap the claim changes in a transaction.
- `UpdatePermissionsRequest { string[] Permissions }` in `StaffDtos.cs`. The list is required and may be empty.
- `AdminStaffTests` extended with 11 tests covering AC 1–3, idempotency, a missing list, and claims of other types being left untouched.
- README section "Staff permissions" describing token behaviour.

## How it works
- **AC 1 (replace):** A PUT with a valid list returns 204. Afterwards the user's `permission` claims equal the new set. Other claim types are not changed. Only the difference is removed and added, inside one transaction.
- **AC 2 (remove all):** An empty list removes every `permission` claim. The user keeps their account. After a new login, calls to permission-protected endpoints return 403.
- **AC 3 (rejected cases):** Checks run in this order:
  1. Permission names are validated against `Permissions.All`. An unknown name returns 400 with error key `Permissions`.
  2. Unknown user id returns 404.
  3. A target that is not in role Staff (Admin or Customer) returns 400.
  4. A caller who is not Admin returns 401 or 403 (enforced by `AdminOnly`).
  
  Every rejection leaves the claims unchanged.
- **Idempotency:** Sending the same set again returns 204 and makes no change.

## How to test
Checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

- **AC 1:** As Admin, `PUT /api/admin/staff/{id}/permissions` with `{"permissions":["ManageShowings"]}`.
  **Verify:** 204, and `GET /api/admin/staff` lists exactly `ManageShowings` for that user.
- **AC 2:** `PUT` with `{"permissions":[]}`, then log in again as that Staff user and call a ManageMovies endpoint.
  **Verify:** 204, no permissions listed, and 403 on the new token.
- **AC 3:** Try a random id, an Admin or Customer id, a non-Admin caller, and `["Foo"]`.
  **Verify:** 404, 400, 403 (or 401), 400 with error key `Permissions`, and claims unchanged.

## Deployment notes
- **Pre-deploy:** none.
- **Post-deploy:** none beyond the normal release.
- **Configuration:** `Jwt:ExpiryMinutes` (default 60) sets how long a token keeps old permissions after a change.
- **Migrations:** none. Permissions use existing claim storage.

## Known limitations
- No immediate token revocation. Changes apply at next login (out of scope for this story).
- No automated test for the 500 path when adding claims fails after the transaction.
- The 404 response title reads "Staff user not found" for any unknown id.
