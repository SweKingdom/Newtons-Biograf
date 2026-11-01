# Project NBR — Delivery Plan

## Summary
- **Scope:** 27 planned items (User Stories US-116 to US-143, excluding US-140 — see Assumptions). Features FT-106 to FT-115 under Epics EP-101 to EP-105.
- **Total effort:** 102 story points (Fibonacci estimates).
- **Plan:** 5 sprints of 2 weeks, capacity 20 points per sprint (max 22 = 110%). Sprint loads: 21 / 21 / 19 / 19 / 22.
- **Key risks:** double-booking protection depends on a database constraint (US-135, 8 pts, largest item); security foundation (US-119/120) gates almost every later story; UI stories depend on API stories in the same or earlier sprint.
- **Project checks:** no build, test or lint checks are configured for this project yet. No check results are reported in this plan.

## Sprint plan

| Sprint | Stories | Points | Goal |
|---|---|---|---|
| 1 | US-116, US-117, US-118, US-119, US-120 | 21 | Solution foundation, data model, design system, customer auth and role policies in place |
| 2 | US-121, US-122, US-123, US-124, US-125, US-126 | 21 | Staff accounts and permissions, login screens, Movie API and screens, Auditorium API with generated seats |
| 3 | US-127, US-128, US-129, US-130, US-131 | 19 | VIP rows and prices, Auditorium screens, showing creation/edit/cancel rules and screens |
| 4 | US-132, US-133, US-134, US-135, US-136 | 19 | Public search and browsing, seat map, booking with double-booking protection, my-bookings API |
| 5 | US-137, US-138, US-139, US-141, US-142, US-143 | 22 | Seat selection and my-bookings screens, cancellation rules and admin screens, xUnit scenario tests, README and test docs |

## Build order (global rank)

| Rank | Story | Pts | Sprint | MoSCoW | Priority | WSJF | Depends on |
|---|---|---|---|---|---|---|---|
| 1 | US-116 | 3 | 1 | Must | 1 | 6.0 | — |
| 2 | US-117 | 5 | 1 | Must | 1 | 3.8 | US-116 |
| 3 | US-118 | 5 | 1 | Must | 2 | 3.2 | US-116 |
| 4 | US-119 | 5 | 1 | Must | 1 | 3.8 | US-117 |
| 5 | US-120 | 3 | 1 | Must | 1 | 7.0 | US-119 |
| 6 | US-121 | 3 | 2 | Must | 2 | 5.3 | US-120, US-117 |
| 7 | US-122 | 2 | 2 | Must | 2 | 6.5 | US-121 |
| 8 | US-123 | 5 | 2 | Must | 2 | 3.0 | US-119, US-121, US-122, US-118 |
| 9 | US-124 | 3 | 2 | Must | 1 | 6.3 | US-117, US-120 |
| 10 | US-125 | 3 | 2 | Must | 2 | 5.3 | US-124, US-123 |
| 11 | US-126 | 5 | 2 | Must | 1 | 3.6 | US-117, US-120 |
| 12 | US-127 | 3 | 3 | Must | 2 | 5.0 | US-126 |
| 13 | US-128 | 3 | 3 | Must | 2 | 5.0 | US-127, US-123, US-118 |
| 14 | US-129 | 5 | 3 | Must | 1 | 4.6 | US-124, US-126, US-127 |
| 15 | US-130 | 5 | 3 | Must | 2 | 3.6 | US-129 |
| 16 | US-131 | 3 | 3 | Must | 2 | 5.0 | US-130, US-128 |
| 17 | US-132 | 3 | 4 | Must | 2 | 6.0 | US-129, US-124 |
| 18 | US-133 | 3 | 4 | Must | 2 | 6.0 | US-132, US-118 |
| 19 | US-134 | 3 | 4 | Must | 1 | 6.7 | US-127, US-129 |
| 20 | US-135 | 8 | 4 | Must | 1 | 3.3 | US-134, US-119, US-117 |
| 21 | US-136 | 2 | 4 | Must | 2 | 7.0 | US-135, US-119 |
| 22 | US-137 | 5 | 5 | Must | 2 | 4.4 | US-135, US-136, US-133, US-118 |
| 23 | US-138 | 5 | 5 | Must | 1 | 4.2 | US-135, US-136, US-130 |
| 24 | US-139 | 3 | 5 | Must | 2 | 5.0 | US-138, US-131 |
| 25 | US-141 | 5 | 5 | Must | 2 | 3.2 | US-129, US-130, US-135, US-138 |
| 26 | US-142 | 2 | 5 | Must | 3 | 5.0 | US-137, US-139 |
| 27 | US-143 | 2 | 5 | Should | 3 | 5.0 | US-141 |

## WSJF method
WSJF = (Business value + Time criticality + Risk reduction/opportunity enablement) / story points. Each component scored 1–10 by the planner. Scores are used to order within dependency constraints; dependencies always take precedence.

## Critical path
US-116 → US-117 → US-119 → US-120 → US-121 → US-122 → US-123 → US-124 → US-126 → US-127 → US-129 → US-130 → US-134 → US-135 → US-136 → US-137 → US-138 → US-139 → US-142

Any slip on this chain moves the go-live date one-for-one. US-135 (8 pts) is the single largest risk on the chain.

## Milestones
- **MVP (end of Sprint 4):** staff can manage movies, auditoriums and showings; customers can browse, see seat maps and book seats; my-bookings API available.
- **UAT (end of Sprint 5 build, before UAT sign-off):** all screens, cancellation rules, scenario tests and README complete; every story ready for UAT.
- **Go-live:** after UAT sign-off, release management smoke test passed, state `Deployed (Done)`.

## Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Double-booking race condition in US-135 | High — customers can book the same seat | Enforce uniqueness at database level (unique constraint per showing and seat) and cover with a concurrency test in US-141 |
| Sprint 5 at 110% of capacity (22 pts) | Medium — spill-over into Sprint 6 | Move US-143 (2 pts) to Sprint 6 first if velocity drops |
| Security foundation (US-119/120) delays block most work | High — Sprints 2–5 blocked | Start US-119 and US-120 first in Sprint 1; keep them at priority 1 |
| Cancellation rules (US-138) depend on booking and showing data | Medium — incorrect 2-hour rule or staff override | Business rules covered by xUnit scenarios in US-141 |
| Missing data for seed and tests (US-140 not in this plan) | Medium — scenario tests and README lack sample data | Track US-140 separately; see Assumptions |
| No project checks configured | High — no automated quality gate | Set up checks through US-116 or a follow-up task; report as not configured until then |

## Assumptions
- Capacity is 20 points per 2-week sprint for the AI team (default); no other capacity was given.
- US-140 (Seed data and committed SQLite database) is excluded from this plan because it was not in the list to plan. It is needed by US-141 and US-142 and should be planned before Sprint 5 closes.
- All stories are MoSCoW Must except US-143 (Should). Priorities were derived from MoSCoW and WSJF.
- WSJF component scores are planner estimates based on the backlog descriptions and the solution design table of contents; no stakeholder scoring was available.
- Stories that share a sprint are ordered by rank; dependencies within a sprint are satisfied by rank order.
