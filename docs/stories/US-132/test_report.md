# US-132 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Default list | ✅ | PublicController.cs; Ac1 tests |
| AC 2 Each filter | ✅ | Genre/date/auditorium/title tests incl. å/ä/ö |
| AC 3 Combined | ✅ | Ac3 test with decoys |
| AC 4 Excluded | ✅ | Ac4 tests, boundaries |
| AC 5 Free seats | ✅ | Ac5 test |
| AC 6 Invalid input | ✅ | Ac6 tests, 400 problem+json |
| AC 7 Staff route | ✅ | Ac7 theory 401/403 |
| run_checks | ✅ | typecheck, test, build exit 0 |

Verification was by static review plus a passing `run_checks`. No screenshots were captured, because this story has no record page.

## Notes
- The date filter uses the `DateTime.TryParseExact` format `yyyy-MM-dd` in the display time zone. This matches the spec.
- The title filter runs in memory, as the spec directs.
