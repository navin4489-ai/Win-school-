# Architecture

## 1. Style

The initial release is a **modular monolith** with clean domain boundaries, as recommended by the BRD
(§11.2). Each module owns its use cases and data; modules communicate through application interfaces,
not by reaching into each other's tables. Boundaries are enforced by project references and namespace
layout, which keeps a later split into services (fees, notifications, analytics) mechanical rather than
a rewrite.

```
Angular SPA / Flutter app
          │  HTTPS + JWT
          ▼
┌────────────────────────────────────────────────────────────┐
│ SchoolErp.Api                                              │
│  Controllers · ProblemDetails · Rate limiter · Swagger     │
├────────────────────────────────────────────────────────────┤
│ SchoolErp.Application                                      │
│  Auth · Admissions · Students · Parents · Fees ·           │
│  Attendance · Exams · Homework · Dashboard · Reports       │
│  DTOs · validation · authorization rules · abstractions    │
├────────────────────────────────────────────────────────────┤
│ SchoolErp.Domain                                           │
│  Entities · enums · domain exceptions · invariants         │
├────────────────────────────────────────────────────────────┤
│ SchoolErp.Infrastructure                                   │
│  EF Core (SQL Server) · JWT · storage · notification       │
│  outbox dispatcher · audit interceptor · seeding           │
└────────────────────────────────────────────────────────────┘
          │
          ▼
   SQL Server · Azure Blob · Redis · SMS/WhatsApp/Email
```

### Dependency rule

`Api → Infrastructure → Application → Domain`. Domain has no framework references. Application
depends only on EF Core's abstractions/data types, so every use case is testable against the EF
in-memory provider.

## 2. Multi-tenancy

Every operational table carries `SchoolId` (required) and `BranchId` (optional). Two mechanisms keep
tenants apart:

1. **Read isolation** — `AppDbContext` applies a global query filter
   `!IsDeleted && (bypass || SchoolId == tenant.SchoolId)`. The filter is resolved from the authenticated
   principal for every request.
2. **Write isolation** — `SaveChanges` stamps `SchoolId` on inserts and rejects updates whose tenant
   does not match the caller.

Authentication queries (`UserAccounts`, `Schools`) explicitly call `IgnoreQueryFilters()` because the
school is not known until after credential validation.

Super Admin is a permission set, not a filter bypass: cross-school actions are performed through
explicitly authorized endpoints and always leave an audit record.

## 3. Authentication and authorization

- Access token: JWT (HS256), 30 minutes, claims `sub`, `email`, `name`, `school_id`, `branch_id`,
  `role`, and one `permission` claim per granted permission.
- Refresh token: 256-bit random value, only its SHA-256 hash is stored, rotated on every use.
  Reuse of a revoked token revokes the whole token family (theft detection).
- Passwords: PBKDF2 via ASP.NET Core `PasswordHasher<UserAccount>`; configurable lockout after five
  failures for fifteen minutes.
- Authorization: a custom `PermissionPolicyProvider` turns any policy name into a
  `PermissionRequirement`, so controllers declare `[Authorize(Policy = Permissions.FeesCollect)]`.
  Role checks are used only for coarse routing; every mutation also checks resource scope
  (assigned class, linked child, own record) inside the service.

### Permission catalogue

Permissions follow `<module>.<action>`: `students.read`, `students.write`, `admissions.manage`,
`fees.read`, `fees.collect`, `fees.refund`, `attendance.read`, `attendance.mark`, `exams.read`,
`exams.manage`, `homework.manage`, `hr.read`, `hr.manage`, `transport.manage`, `library.manage`,
`inventory.manage`, `reports.read`, `settings.manage`, `users.manage`, `audit.read`.
Roles are data, not code: `RolePermissions` rows are seeded and can be edited by a Super Admin.

## 4. Data model

- Integer surrogate keys; `AdmissionNo`, `EmployeeNo`, `ReceiptNo` and application numbers are
  human-readable, school-scoped unique values.
- All operational entities inherit `AuditableEntity`: `SchoolId`, `BranchId`, `CreatedAtUtc`,
  `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy`, `IsDeleted`, `RowVersion`.
- Deletes are soft. A global filter hides deleted rows; `IgnoreQueryFilters` is reserved for
  maintenance endpoints.
- Financial data is append-only: payments and refunds are never edited, only reversed by a new
  compensating record. Installment balances are derived from payments, not stored twice.
- `AuditLogs` records actor, IP, action, entity, entity id, before/after JSON and timestamp for every
  sensitive mutation; secrets and password hashes are scrubbed before serialisation.

See [`er-diagram.md`](er-diagram.md) for the relationship overview and `database/schema.sql` for the
physical model.

## 5. Key workflows

### Admission → enrollment

```
Enquiry → Application → Document verification → Interview → Approval
        → Fee payment → Enrollment (transaction) → Student + Parent link
        → Student ID → Parent credentials
```

`AdmissionService.EnrollAsync` runs in a database transaction. It creates the student, links or creates
guardians, assigns the class/section, marks the application `Enrolled`, optionally creates a parent
login, writes an audit record and queues a confirmation notification. Re-running enrollment is
idempotent: the admission status guard rejects a second enrollment.

### Fee collection

```
Structure (session + class) → Student assignment → Installments
   → Payment (idempotency key) → Gateway verification → Receipt
   → Ledger update → Dashboard
```

Receipt numbers are generated per school per day (`RCPT-{schoolCode}-{yyyyMMdd}-{sequence}`) inside a
transaction. A unique filtered index on `ReceiptNo` plus the gateway transaction id makes double
posting impossible. Failed gateway callbacks are stored as `PaymentTransactions` with status
`Failed` and never move a balance.

### Attendance

```
Teacher → class/section → roster → mark → submit
   → scope check (TeacherAssignment) → upsert → audit
   → absence notifications (outbox) → summaries
```

Bulk marking is limited to the teacher's assigned classes; administrators with `attendance.mark` can
override, and every override is audited.

### Examination

```
Exam + subjects → marks entry → validation → approval → result processing
   → grades/percentage/rank → report card (HTML/PDF) → publish → notify
```

Marks are frozen once the exam is approved; corrections require an explicit reopen that is audited.

## 6. Cross-cutting concerns

| Concern | Approach |
| --- | --- |
| Validation | Data annotations on requests + guard clauses and domain exceptions in services |
| Errors | Consistent `ProblemDetails` with `code`, `traceId`, field errors |
| Pagination | `page`, `pageSize` (max 100) with `PagedResult<T>` |
| Caching | Redis for dashboard aggregates and permission lookups (opt-in via config) |
| Background work | Outbox dispatcher hosted service; Hangfire/Azure Functions for heavy jobs |
| Files | `IFileStorage` abstraction; local disk in dev, Azure Blob in production |
| Observability | Structured logs, correlation id, health checks, Application Insights |
| Time | All persistence in UTC; school time zone only for presentation |

## 7. Deployment topology

```
Azure Front Door / App Gateway (TLS, WAF)
        │
        ├── Static Web App / App Service (Angular)
        └── App Service or Container Apps (API, autoscale)
                 ├── Azure SQL (zone-redundant)
                 ├── Azure Blob Storage (documents)
                 ├── Azure Cache for Redis
                 ├── Azure Communication Services (email/SMS)
                 └── Application Insights + Azure Monitor
```

Secrets live in Key Vault and are injected as App Service settings or Container App secrets. CI/CD
pipelines build, test, scan and deploy through staging slots with approval gates
(`.github/workflows/ci.yml`, `infra/azure-pipelines.yml`).

## 8. Decisions and trade-offs

Recorded as ADRs in [`adr/`](adr/):

- Modular monolith first, services later.
- Permission policies over role checks.
- Global query filters plus explicit write guards for tenancy.
- Soft delete everywhere except audit/ledger tables.
- Payment gateway behind `IPaymentGateway` so Razorpay/Cashfree can be swapped per school.
