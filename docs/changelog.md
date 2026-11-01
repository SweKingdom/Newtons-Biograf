# Changelog

## Unreleased

- 2026-11-01 — US-122 Admin changes and removes staff permissions: `PUT /api/admin/staff/{id}/permissions` replaces a Staff user's permission set (204, idempotent, transactional); changes apply at next login.
