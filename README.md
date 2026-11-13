# School ERP — Complete School Management System

A multi-tenant school ERP covering admissions, student information, fees, attendance, academics,
examinations, HR/payroll, transport, library, inventory, communication and analytics.

Built for **Wintech Innovations** from the *School Management System BRD v1.0*.

| Layer | Technology |
| --- | --- |
| Admin / Parent / Teacher web | Angular 18 (standalone, signals) |
| API | ASP.NET Core 8 Web API, C# 12 |
| Data | SQL Server + EF Core 8 |
| Mobile | Flutter 3 (Android + iOS) |
| Cloud | Azure App Service / Container Apps, Blob Storage, Redis, Azure Monitor |
| Auth | JWT access + rotating refresh tokens, permission policies |

## Repository layout

```
.
├── backend/
│   ├── SchoolErp.sln
│   ├── src/
│   │   ├── SchoolErp.Domain/          # Entities, enums, domain rules (no dependencies)
│   │   ├── SchoolErp.Application/     # Use cases, DTOs, abstractions (Phase 1–6 implemented)
│   │   ├── SchoolErp.Infrastructure/  # EF Core, JWT, storage, notifications, seeding
│   │   └── SchoolErp.Api/             # Controllers, middleware, Swagger, health checks
│   └── tests/SchoolErp.Tests/         # Service + tenancy tests (xUnit, EF InMemory)
├── frontend/admin-portal/             # Angular 18 SPA (admin, teacher, parent, accountant views)
├── mobile/school_erp_app/             # Flutter application (parent, teacher, student, admin)
├── database/                          # SQL Server DDL + permission catalogue seed (production schema)
├── docs/                              # Architecture, ERD, API contract, security, UAT, roadmap
├── infra/                             # Dockerfiles, nginx, docker-compose, Azure DevOps pipeline
└── .github/workflows/ci.yml           # Build + test pipeline
```

## Delivery status

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | Foundation, auth, roles, permissions, school setup | **Implemented** |
| 2 | Admission, student, parent management | **Implemented** |
| 3 | Fees, payments, ledger, receipts, refunds | **Implemented** (dummy gateway; swap in a real PSP) |
| 4 | Attendance + academic structure | **Implemented** (student + staff attendance with photo and location) |
| 5 | Examinations, timetable, homework | **Partially implemented** — exams, marks, report cards, homework CRUD |
| 6 | HR, payroll | **Scaffolded** — employee directory, leave, payroll tables + read APIs |
| 7 | Transport, library, inventory | **Scaffolded** — schema + read APIs, UI placeholders |
| 8 | Mobile apps + notifications | **Implemented** (Flutter shell, live notification stream, outbox dispatcher) |
| 9 | Dashboards, reports, analytics | **Implemented** — role dashboards, CSV reports |
| 10 | Hardening, UAT, deployment | **Runbook ready** — see `docs/security.md`, `docs/deployment.md`, `docs/uat-checklist.md` |

Anything marked *scaffolded* has database schema, permissions, navigation and API surface in place
but intentionally minimal business logic. See `docs/roadmap.md` for the module-by-module backlog.

### Late fee policy

Fees must be paid by the **15th** of the month the fee falls due (a fee due after the 15th is
payable by the 15th of the following month). From the next day a late fee of **₹10 per day**
accrues and the guardian is notified. The total late fee of one installment is capped at
**25 % of that installment** so a long-dormant fee cannot collect a penalty larger than the
school expects to recover. The policy is configurable under `Fees:Penalty` in
`appsettings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `DailyAmount` | `10` | Late fee charged per day |
| `PayByDay` | `15` | Day of the month the fee must be paid by |
| `MaxPercentageOfFee` | `25` | Cap on the late fee of one installment, in percent |
| `IntervalMinutes` | `60` | How often the penalty job runs |

The job writes one row per installment per day in `FeePenalty` and a unique index on
`(FeeInstallmentId, PenaltyDate)` keeps it idempotent, so a restart can never double charge.

### Live notifications

Notifications are published to the browser the moment they are written and raise a popup with a
bell tone. The portal subscribes to `GET /api/notifications/stream` (server-sent events) and
falls back to polling if the connection drops. The stream is process local — a multi-node
deployment must fan out through Redis pub/sub or Azure SignalR Service, see
`docs/deployment.md`.

## Quick start

### Prerequisites

- .NET SDK 8.0
- Node.js 20+ / npm 10+
- SQL Server 2019+ (or `docker compose -f infra/docker-compose.yml up -d sqlserver`)
- Flutter 3.22+ (mobile only)

### 1. Database

Production / shared environments:

```bash
sqlcmd -S localhost -U sa -P 'Your_password123' -i database/schema.sql
sqlcmd -S localhost -U sa -P 'Your_password123' -d SchoolErp -i database/seed.sql
```

Local development can instead let EF Core create the schema on first run (`Database:AutoCreate=true`,
the default in Development). Add migrations once the model is final:

```bash
cd backend
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src/SchoolErp.Infrastructure -s src/SchoolErp.Api
```

### 2. API

```bash
cd backend
dotnet restore
dotnet run --project src/SchoolErp.Api
```

- API: <http://localhost:5080/api> (`--launch-profile https` also serves <https://localhost:7080>)
- Swagger: <http://localhost:5080/swagger>
- Health: <http://localhost:5080/health>
- Demo data is seeded on first development start.

Run tests with `dotnet test backend/SchoolErp.sln`.

### 3. Web portal

```bash
cd frontend/admin-portal
npm install
npm start
```

Open <http://localhost:4200>. The dev server proxies `/api` and `/files` to `http://localhost:5080` (`proxy.conf.json`).

### 4. Mobile app

```bash
cd mobile/school_erp_app
flutter create . --org com.wintech --project-name school_erp_app   # first time only: generates android/ and ios/
flutter pub get
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080/api
```

### End-to-end tests

With the API running (`scripts/dev-api.sh` starts it on an in-memory demo database), run:

```bash
node tests/e2e/api-e2e.mjs http://localhost:5080
```

The suite makes 120 checks. It covers every module workflow (admission → enrolment, fees, refunds, attendance, the exam lifecycle, homework, HR, library, transport, inventory, LMS, notifications, reports). It also checks role isolation: parents see only their linked children, and students see only themselves.

### Android test build

Each push that changes `mobile/` builds an APK with the **Mobile APK** workflow and publishes it to the
[`mobile-latest` release](../../releases/tag/mobile-latest). Install it on an Android phone, then enter
the API address on the login screen (for example `https://erp.yourschool.in` or `192.168.1.20:5080`).

### Demo accounts

| Role | Email | Password |
| --- | --- | --- |
| Super Admin | `superadmin@demo.edu` | `Passw0rd!` |
| Principal | `principal@demo.edu` | `Passw0rd!` |
| Admission Manager | `admissions@demo.edu` | `Passw0rd!` |
| Accountant | `accounts@demo.edu` | `Passw0rd!` |
| Teacher | `teacher@demo.edu` | `Passw0rd!` |
| Parent | `parent@demo.edu` | `Passw0rd!` |
| Student | `student@demo.edu` | `Passw0rd!` |
| HR Manager | `hr@demo.edu` | `Passw0rd!` |
| Librarian | `librarian@demo.edu` | `Passw0rd!` |
| Transport Manager | `transport@demo.edu` | `Passw0rd!` |

> Demo credentials are seeded **only** in the Development environment with `Seed:Enabled=true`. Never enable them in production.

### Docker (full stack)

```bash
docker compose -f infra/docker-compose.yml up --build
```

Web portal: <http://localhost:8080>. API: <http://localhost:5080>. SQL Server: `localhost:1433`.

## Multiple schools (super admin)

One portal and one app serve many schools. Every record belongs to one school, and each request only ever sees its own school's data.

- **Schools** (super admin only): create a school with its code, logo, address and admin account. Choose which modules it uses, and suspend or reactivate it. A new school gets a main campus, the current April–March session and the 13 standard roles.
- **Users & roles** (`users.manage`): create users, change their role, disable them and reset passwords. A school admin manages only their own school. The super admin can pick any school.
- **Sign-in**: users enter their school code. If the same email exists in two schools, sign-in asks for the code instead of guessing.
- **Isolation**: a request without a school sees nothing. Another school's records return 404. Only the super admin, through the platform endpoints, can change another school's rows.
- **Modules**: switched-off modules disappear from menus and their endpoints return 403. The change applies at the user's next sign-in.
- API: `api/platform/modules`, `api/platform/schools[/{id}[/active]]`, `api/users[/{id}[/reset-password]]`, `api/users/roles`.

## Security highlights

- JWT access tokens (30 min) + hashed, rotating refresh tokens (7 days) with reuse detection.
- Permission policies at module *and* action level (`students.read`, `fees.collect`, …).
- School/branch isolation through EF Core global query filters plus write-time tenant guards.
- Teacher scope limited to assigned class/section/subject; parents limited to linked children.
- Parents can only raise and complete payments for their own children, never for anyone else's.
- Account lockout, rate limiting, soft delete, optimistic concurrency, before/after audit trail.
- Upload allow-list, size limits and a malware-scanning abstraction; sensitive fields masked in logs.
- OWASP-aligned validation, parameterised queries, CORS allow-list and TLS-only transport.

Full control mapping: [`docs/security.md`](docs/security.md).

## Push to GitHub

This repository is committed locally on `main`. GitHub access is blocked from the build sandbox,
so create the remote and push from a machine with your credentials:

```bash
gh auth login
gh repo create <your-github-username>/school-erp --private --source=. --remote=origin --push
```

Without the GitHub CLI:

```bash
git remote add origin https://github.com/<owner>/<repo>.git
git push -u origin main
```

GitHub Actions (`.github/workflows/ci.yml`) builds and tests the API, web portal and mobile app on every push. `infra/azure-pipelines.yml` is the Azure DevOps equivalent and also publishes container images. See [`docs/deployment.md`](docs/deployment.md) for Azure provisioning.

## Documentation

- [Architecture](docs/architecture.md)
- [Entity relationship overview](docs/er-diagram.md)
- [API contract](docs/api-contract.md)
- [Security controls](docs/security.md)
- [Deployment runbook](docs/deployment.md)
- [UAT checklist](docs/uat-checklist.md)
- [Roadmap and backlog](docs/roadmap.md)
- [BRD summary and traceability](docs/brd-summary.md)

## Licence

Proprietary — © Wintech Innovations. All rights reserved.
