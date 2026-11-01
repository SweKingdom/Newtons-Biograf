# US-131 — Test report (iteration 1)
**Verdict:** ✅ Approved · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| AC 1 Schedule | ✅ | ShowingForm.razor preview = start + length + cleaning; settings endpoint anonymous (tested) |
| AC 2 Conflict shown | ✅ | StartTime error on Date field; overlap detail in AlertBox above form; values kept |
| AC 3 Cancel | ✅ | Inline confirmation with ActiveBookingCount; POST cancel; reload |
| AC 4 Edit / delete | ✅ | Edit disabled with tooltip; Delete only at 0 bookings; 409 shown |
| AC 5 Access | ✅ | Page policy ManageShowings (tested), nav in AuthorizeView |
| Checks (typecheck, test, build) | ✅ | all exit 0 |

Verification was by static review plus test output; no screenshots were captured.

## Not tested
- Rendered UI behaviour (no bUnit configured, as the story notes). Page and form behaviour is reviewed from code only, so a manual run per How to test is advisable in UAT.

## Notes
- The not-authorized message relies on the existing `AccessRedirect` component.
