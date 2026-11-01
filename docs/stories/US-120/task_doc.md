# US-120: Role and permission authorization policies (401/403)
**Status:** Ready for UAT · **Category:** security · **Points:** 3 · **Sprint:** not recorded

## Summary
Every protected API endpoint can now be restricted by role or permission. Unauthenticated calls get 401 and users without the required role or permission get 403, both as problem+json bodies.

## What was built
- `Permissions.cs` (`Api/Auth`): `Permissions`, `Roles` and `Policies` constants and `AddAppPolicies` to register the policies.
- `TokenService.cs`: `PermissionClaimType` now references `Permissions.ClaimType`; new `RoleClaimType = "role"`.
- `Program.cs`: JWT bearer authentication (validation parameters from `Jwt` options, `MapInboundClaims=false`), `OnChallenge` and `OnForbidden` handlers that write problem+json, `AddAuthorization`, `UseAuthentication` and `UseAuthorization`.
- `NewtonsBiograf.Api.csproj`: package `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.10.
- `AuthorizationTests.cs` (tests): probe controller `PolicyProbeController`, policy and status matrix tests, problem+json tests.

## How it works
- **AC 1 (policies exist):** `AdminOnly`, `CustomerOnly`, `ManageMovies`, `ManageShowings` and `ManageBookings` are registered and usable with `[Authorize(Policy=...)]`.
- **AC 2 (401 vs 403):**
  - No token, expired token or wrong-key token → 401.
  - Customer on a staff endpoint → 403.
  - Staff without the claim → 403; Staff with the claim → allowed.
  - Admin → allowed on `AdminOnly` and on all permission policies.
- **AC 3 (JSON error body):** 401 and 403 return `application/problem+json` with `status` and `title`. No redirect and no `Location` header.

## How to test
Run `npm run typecheck --silent`, `npm run test --silent` and `npm run build --silent` through `run_checks`.
- **AC 1:** Run `Ac1_Policy_is_registered`. **Verify:** all five policies resolve from `IAuthorizationPolicyProvider`.
- **AC 2:** Run `Ac2_Status_matrix` and `Ac2_Expired_token_...`. **Verify:** no token, expired token or wrong-key token returns 401; Customer or Staff without the claim returns 403; Staff with the claim returns 200; Admin returns 200 on `AdminOnly` and the permission policies.
- **AC 3:** Run `Ac3_401_...` and `Ac3_403_...`. **Verify:** content type is `application/problem+json`, `status` and `title` are in the body, and there is no `Location` header.

## Deployment notes
- No manual steps identified. Signing key must be present in the `Jwt` configuration; the API validates it on start.

## Known limitations
- Permission constants live in `Api/Auth/Permissions.cs`; the design calls for `Shared/Permissions.cs`. The client will need a shared copy when it uses them.
- The probe controller exists only in the test project (added via `AddApplicationPart`) and is not shipped.
- Endpoint-specific authorization is applied in each feature story, not here.
- Role claim is written as `role` (short name), not `ClaimTypes.Role`.
