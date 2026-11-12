# ADR 0002 — Permission-based authorization

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

The BRD requires configurable module- and action-level permissions for 14 roles, with school-level
data isolation. Role-name checks scattered through controllers do not satisfy that requirement and
break as soon as a school edits a role.

## Decision

Model permissions as data (`Permissions`, `RolePermissions`) and embed granted permission codes as JWT
claims. A custom policy provider resolves `[Authorize(Policy = "fees.collect")]` into a permission
requirement. Services additionally enforce resource scope (own child, assigned class, own record).

## Consequences

- Authorization is declarative and auditable; new permissions need no policy registration.
- Token size grows with permissions (bounded: 22 permissions today).
- Permission changes take effect on the next token refresh; a `token_version` claim allows forced
  re-issue after sensitive role changes.
- Resource scope cannot live in policy alone, so every service method validates ownership.
