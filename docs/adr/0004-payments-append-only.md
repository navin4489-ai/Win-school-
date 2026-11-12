# ADR 0004 — Append-only financial ledger and idempotent receipts

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Fee collection is the highest-risk workflow: duplicate receipts, failed gateway callbacks and refunds
must never corrupt balances (BRD §14, §17).

## Decision

Payments and refunds are append-only. Every payment carries a client idempotency key and unique
receipt number per school; gateway attempts are stored separately with their own status. Balances are
derived (assignment total minus successful payments minus approved discounts plus refunds). Refunds
require an approved-by user and create a compensating entry.

## Consequences

- Reporting queries aggregate payments; no balance drift from partial writes.
- A unique filtered index on `(SchoolId, ReceiptNo)` and `(SchoolId, IdempotencyKey)` enforces safety
  even under concurrent requests.
- Corrections are visible in the ledger, which is desirable for audit.
