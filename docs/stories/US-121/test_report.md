# US-121 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Create staff | ✅ | AdminStaffController.Create; Ac1_* tests (incl. rollback 500, no permissions) |
| AC 2 Non-admin rejected | ✅ | AdminOnly policy; Ac2 theories 401/403/403/Admin for POST and GET |
| AC 3 Validation | ✅ | Per-field errors Permissions/Password/Email/Name; no user created |
| AC 4 List staff | ✅ | Staff only, ordered by name, with permissions; empty list |
| Checks gate | ✅ | typecheck, test, build exit 0 |

Evidence is by static review of the delivered files and the passing run_checks. No screenshots were captured, because this story is API-only.

## Notes
- Permission matching is case-sensitive, and the tests cover it.
- No secrets or injection risks found. Identity handles password hashing.

## Not tested
- None.
