# US-126 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Create | ✅ | AuditoriumsController.Create; Ac1_* tests |
| AC 2 Unique name | ✅ | NameTaken (OrdinalIgnoreCase, trimmed, excludes self); Ac2_* tests |
| AC 3 Validation | ✅ | Ac3_* tests (boundaries, binding failures, PUT) |
| AC 4 View | ✅ | Ac4_* tests |
| AC 5 Edit | ✅ | Ac5_* tests (ids kept, VIP row type inherited) |
| AC 6 Edit safety | ✅ | HasBookings runs before any mutation; Ac6_* tests |
| AC 7 Permissions | ✅ | Ac7 theory (401/403/admin) |
| Checks gate | ✅ | typecheck, test and build all exit 0 |

This is an API-only story, verified by static review plus test output. No screenshots were taken.

## Notes (not failures)
- The PO's answer says the anonymous GET returns only id, name and capacity. The AC returns Rows, SeatsPerRow and VipRows as well. The delivery follows the AC. Please confirm with the PO that the extra fields are acceptable, since they expose no personal data.
- The PO's answer says the 409 message names the blocking showings. The AC fixes the message text, and the delivery uses the AC text.
- The unique-index race path in `TrySave` has no test. It matches on the inner message containing "Auditorium.Name", which depends on the SQLite message format and was not verified.

## Not tested
- The race path described above.
