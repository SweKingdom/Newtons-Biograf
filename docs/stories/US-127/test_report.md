# US-127 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Mark row VIP | ✅ | SetRowType; Us127_Ac1_* tests |
| AC 2 Invalid input | ✅ | 404/400/401/403 tests Us127_Ac2_* |
| AC 3 Prices from config | ✅ | PricingTests Ac3_* (in-memory override) |
| AC 4 No hard-coded prices | ✅ | Ac4_Production_code_has_no_price_literals |
| AC 5 Startup validation | ✅ | Program.cs ValidateOnStart; Ac5 theory |
| AC 6 Past bookings unaffected | ✅ | Us127_Ac6 test |
| Checks (typecheck, test, build) | ✅ | run_checks all exit 0 |

Method: static review of the delivered files plus a passing check run. No screenshots were captured.

## Notes
- The price snapshot at booking creation (AC 6) is enforced by the booking story. This story proves only that a row type change leaves existing bookings untouched.
- The AC 4 scan ignores lines containing MaxLength or StringLength, and skips Migrations.

## Not tested
- None.
