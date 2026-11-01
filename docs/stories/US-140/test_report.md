# US-140 — Test report (iteration 1)
**Verdict:** ❌ Changes requested · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build) | ✅ | all exit 0 |
| AC 1 Accounts | ✅ | DbSeeder.Accounts |
| AC 2 Content counts | ✅ | AuditoriumSeeds, MovieSeeds, showing loop, BookingSeeds |
| AC 3 Valid data | ✅ | overlap guard, End formula, prices |
| AC 4 Idempotent | ✅ | natural-key guards, firstRun |
| AC 5 DB committed | ❌ | `src/NewtonsBiograf.Api/newtons.db` is not on the branch |

## What to do
- AC 5: generate `newtons.db` (delete local `newtons.db*`, run the API in Development once, stop it) and commit `src/NewtonsBiograf.Api/newtons.db`. Do not commit `*.db-shm` or `*.db-wal`. Verify with `git ls-files src/NewtonsBiograf.Api/newtons.db`.

## Not tested
- Fresh-clone manual run (needs the committed file).
