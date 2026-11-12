# ADR 0003 — Tenant isolation with global query filters and write guards

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

One deployment serves multiple schools/branches (BRD §9.2, §10). Missing a `WHERE SchoolId = ...`
clause would leak student data.

## Decision

Apply an EF Core global query filter on every `AuditableEntity`. Stamping and verification happen in
`SaveChanges`: inserts receive the caller's `SchoolId`; updates with a foreign tenant throw. Login and
seeding use explicit `IgnoreQueryFilters()`.

## Consequences

- Default-safe reads; raw SQL and `IgnoreQueryFilters` are code-review hotspots.
- Background jobs must run under a system tenant context (resolved per tenant) rather than bypassing
  silently.
- Tests assert cross-tenant access returns 404/empty rather than data.
