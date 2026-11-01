# US-130 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Edit | ✅ | ShowingsController.Update; Us130_Ac1_* tests |
| AC 2 Cancel | ✅ | ShowingService.CancelShowingAsync, BookingService.CancelInternalAsync; Us130_Ac2_* |
| AC 3 Delete rules | ✅ | ShowingsController.Delete; Us130_Ac3_* |
| AC 4 Atomic | ✅ | Transaction + UseExceptionHandler; failing-BookingService test |
| AC 5 Edit blocked | ✅ | Update 409 guards; Us130_Ac5_* |
| AC 6 Events | ✅ | Events after commit; Us130_Ac6_* |
| AC 7 Permissions | ✅ | Controller [Authorize(ManageShowings)]; Us130_Ac7 theory |
| run_checks (typecheck, test, build) | ✅ | all exit 0 |

## Notes
- Static review only; no screenshots were captured.
- A subscriber that throws after commit is logged and does not undo the cancellation (reasonable, tested).
