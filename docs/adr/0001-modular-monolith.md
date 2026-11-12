# ADR 0001 — Modular monolith before microservices

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

The BRD asks for a broad ERP delivered in 27 weeks across 22 modules. A service-per-module topology
would multiply infrastructure, contracts and operational work before product-market fit is proven.

## Decision

Ship a modular monolith: one deployable API with strict internal module boundaries (Domain,
Application, Infrastructure, Api). Modules expose application services and integration events rather
than sharing tables. Cross-module reads go through query contracts.

## Consequences

- Single transaction for admission/fee/attendance workflows, which matches school operations.
- One pipeline, one database, cheap scaling for the expected tenant size.
- Boundaries must be policed in review; the namespace layout and project references make violations
  visible.
- Extraction candidates are already isolated: notifications, reporting/analytics, payment gateway and
  document generation.
