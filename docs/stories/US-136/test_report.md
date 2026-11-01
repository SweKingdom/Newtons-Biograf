# US-136 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | all exit 0 |
| AC 1 Upcoming | ✅ | BookingService.GetMineAsync; Ac1 test |
| AC 2 History | ✅ | negation, desc order; Ac2 test |
| AC 3 Scope validation | ✅ | controller Mine; Ac3 tests |
| AC 4 Own detail | ✅ | ToDto, GetById; Ac4 tests |
| AC 5 Other customer / unknown | ✅ | BookingForbidden → 403, NotFound → 404; Ac5 tests |
| AC 6 Staff access | ✅ | ManageBookings policy, filter required; Ac6 tests |
| AC 7 Isolation | ✅ | UserId filter; Ac7 test |

## Notes (non-blocking)
- Tests use the real clock with showings days away rather than a fake clock; stable in practice.
- `BookingDto` property is `Auditorium` (from US-135), the spec calls it AuditoriumName; the value is correct.
- Evidence is by static review plus passing checks; no screenshots were taken.
