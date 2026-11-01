# Release 0.5.0 — 2026-11-02

## Highlights
- Reviewers can start the app from a fresh clone and log in at once: a committed, seeded SQLite database with six demo accounts.
- Development start migrates and seeds the database; existing data is never overwritten (idempotent seed, showings topped up for days without any).
- The README gives a run guide, the seeded accounts, and three step-by-step user scenarios with a map to the code for generics, delegates/events, LINQ and async.

## Delivered
| Code | Title | Category | Points |
|---|---|---|---|
| US-140 | Seed data and committed SQLite database | data_migration | 3 |
| US-142 | README with run guide, accounts and three user scenarios | documentation | 2 |

## Checks
Run on the base branch in this release run:
| Check | Result |
|---|---|
| install (`npm install --no-audit --no-fund`) | ✅ up to date |
| typecheck (`npm run typecheck --silent`) | ✅ exit 0 |
| test (`npm run test --silent`) | ✅ exit 0 |
| build (`npm run build --silent`) | ✅ exit 0 |

## Smoke test results
| Code | Result | Note |
|---|---|---|
| US-140 | ✅ | Seeder, Development startup hook, committed `newtons.db` and SeedTests present on base. Live login not run by Release; covered by SeedTests AC 1–4. |
| US-142 | ✅ | README sections 1–4 checked against the project; sections 5–8 checked by QA. Fresh-clone run not executed by Release. |

## Known issues
- US-140 AC 5 (fresh-clone run) is verified manually only: the committed `newtons.db` is in the repository, but a fresh clone has not been started in this release run.
- US-142 AC 4 (follow the README on a fresh clone) is not executed in this release run.
- Seeded showings cover the 14 days from the seed date. Development startup tops up days that have no showings; the committed database is a snapshot and goes stale over time (PO decision recorded in US-140).
- Test-host teardown flake in `AuditoriumTests` (FileSystemWatcher) was fixed by disabling `reloadConfigOnChange` in `TestEnvironment.cs`; it did not recur in this run.

## Post-release steps
- None required for production. The seed runs only in Development; set `Seed:Enabled=false` to switch it off. Demo password (`Seed:Password`) is for demo use only.
- If `newtons.db` is ever regenerated, follow the manual steps in US-140 and commit only `newtons.db` (not `*.db-shm`/`*.db-wal`).

## Rollback plan
- Previous tag: none (first tagged release, 0.5.0). Rollback target is the commit before the US-140 and US-142 merges on the base branch.
- Migrations: none added by this release beyond the existing InitialCreate; no down step needed.
- Data: `newtons.db` is demo data only; restoring the previous committed file restores the seed state.
- Decision: the product owner decides on rollback.