# US-123 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | all exit 0 |
| AC 1 Register and login | ✅ | Register.razor, Login.razor, MainLayout.razor, ClientAuthTests |
| AC 2 Admin staff screen | ✅ | Admin/Staff.razor |
| AC 3 Logout and guard | ✅ | JwtAuthStateProvider, App.razor, AccessRedirect.razor, AuthMessageHandler |

## Checks
Static review of delivered files against each AC; no screenshots (no running UI in this environment).
- **AC 1:** per-field API errors under FormField via ApiProblem; general errors (e.g. "Invalid email or password.") in AlertBox; header shows name and role; Staff link only for Admin.
- **AC 2:** admin-only page with staff table, create form, three permission checkboxes, edit and Save via the API; failure paths handled.
- **AC 3:** Logout removes `nb_token`; anonymous users redirected to login with a safe returnUrl; forbidden users go to /no-access; 401/403 handled in the handler.

## Not tested
- Manual browser flow (not runnable here).
- Notes: no component-level test of the Staff save flow; Staff-permission menu items deferred by design.
