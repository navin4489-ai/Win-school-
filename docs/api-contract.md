# API contract

Base path: `/api` (local: `http://localhost:5080/api`). Swagger UI: `/swagger`.

## Conventions

- **Auth**: `Authorization: Bearer <accessToken>` from `POST /api/auth/login`. Access tokens last 30 minutes; call `POST /api/auth/refresh` with `{ "refreshToken": "..." }` to rotate. Reusing an old refresh token revokes the chain.
- **JSON**: camelCase properties, enums as strings (`"Present"`, `"Cash"`), dates as `yyyy-MM-dd`, timestamps as UTC ISO-8601. Null properties are omitted.
- **Paging**: list endpoints take `page` (≥1) and `pageSize` (1–100, default 20) and return `{ items, page, pageSize, totalCount, totalPages }`.
- **Errors**: RFC 7807 problem details with `status`, `title`, `detail` and a `code` (`not_found`, `validation`, `conflict`, `forbidden`, …).
- **Authorization**: permission policies `permission:<module>.<action>`. Parents and students are further limited to their linked or own records; unlinked IDs return `404`.
- **Tenancy**: the school comes from the token. A client can't pick another school.

## Endpoints

| Area | Method & path | Permission |
| --- | --- | --- |
| Auth | `POST auth/login`, `POST auth/refresh` | anonymous |
| | `POST auth/logout`, `GET auth/me`, `POST auth/change-password` | signed in |
| Dashboard | `GET dashboard` (role-aware cards and trends) | signed in |
| Administration | `GET/PUT admin/school`; `GET/POST/PUT admin/branches`, `admin/sessions` (+`POST sessions/{id}/close`), `admin/classes`, `admin/classes/{classId}/sections`, `admin/subjects`, `admin/classes/{classId}/subjects`, `admin/timetable`, `admin/announcements` | `settings.manage` (reads broader) |
| | `GET admin/roles`, `GET admin/permissions`, `PUT admin/roles/{roleId}/permissions` | `users.manage` |
| Admissions | `GET admissions?search&status&page`, `GET/POST admissions`, `POST admissions/{id}/submit|verify-documents|schedule-interview|approve|reject|waitlist|enroll` | `admissions.manage` |
| Students | `GET students?search&classId&sectionId&status`, `GET/PUT students/{id}` | `students.read` / `students.write` |
| | `POST students/{id}/promote|withdraw|documents`, `POST students/{id}/certificates/{type}` (file download) | `students.write` |
| Parent portal | `GET parent/children`, `GET parent/children/{studentId}/overview|attendance|fees|results` | role `Parent` |
| Fees | `GET/POST fees/categories`, `GET/POST fees/structures`, `POST fees/assignments` | `fees.read` / `fees.collect` |
| | `GET fees/students/{studentId}/ledger`, `GET fees/payments` | `fees.read` (scoped for parents/students) |
| | `POST fees/payments` (send `idempotencyKey`) | `fees.collect` |
| | `POST fees/payments/order`, `POST fees/payments/online` (gateway checkout) | `fees.read` — parents for own children only |
| | `GET fees/payments/{id}/receipt`, `GET fees/students/{studentId}/statement` (printable HTML) | `fees.read` (scoped) |
| | `POST fees/payments/{id}/refunds`, `POST fees/refunds/{id}/approve` | `fees.refund` |
| | `GET fees/summary?from&to`, `GET fees/outstanding?classId`, `POST fees/reminders` | staff only |
| Attendance | `GET attendance/roster?classId&sectionId&date`, `POST attendance/mark` | `attendance.read` / `attendance.mark` |
| | `GET attendance/students/{studentId}?from&to` | `attendance.read` (scoped) |
| | `GET attendance/staff?date` | principal or `hr.read` |
| | `POST attendance/staff/check-in|check-out` (`photoUrl`, `latitude`, `longitude`, `address`), `GET attendance/staff/me?from&to` | `attendance.mark` / signed-in staff |
| Files | `POST files/upload` (multipart, JPG/PNG/WEBP/HEIC/PDF) | signed in |
| Assignments | `GET/POST/PUT/DELETE admin/teacher-assignments` | `settings.manage` |
| Exams | `GET/POST exams`, `GET/POST exams/{id}/subjects`, `GET exams/subjects/{examSubjectId}/marks`, `POST exams/{id}/marks|submit|approve|publish` | `exams.read` / `exams.manage` |
| | `GET exams/{id}/results`, `GET exams/{id}/report-cards/{studentId}` (`{ html, ... }`) | `exams.read` (scoped) |
| Homework | `GET/POST homework`, `GET/POST homework/{id}/submissions`, `POST homework/submissions/{id}/grade` | signed in / `homework.manage` |
| HR & payroll | `GET/POST hr/employees`, `GET hr/departments|designations`, `GET/POST hr/leaves`, `POST hr/leaves/{id}/approve`, `GET/POST hr/payroll`, `GET hr/payroll/{runId}/payslips` | `hr.read` / `hr.manage` |
| Transport | `GET/POST transport/vehicles|routes|allocations` | `transport.manage` |
| Library | `GET/POST library/books`, `POST library/issues|returns`, `GET library/transactions` | `library.manage` |
| Inventory | `GET/POST inventory`, `POST inventory/stock` | `inventory.manage` |
| LMS | `GET/POST lms/materials` | `lms.manage` |
| Notifications | `GET notifications?unreadOnly`, `GET notifications/unread-count`, `POST notifications/{id}/read`, `POST notifications/read-all` | signed in (own only) |
| | `GET notifications/stream` (server-sent events, live popups) | signed in (own only) |
| | `POST notifications/broadcast` | `communication.manage` |
| Reports | `GET reports/types`, `GET reports/export?type&from&to&classId` (CSV, audited) | `reports.read` |
| Health | `GET /health`, `GET /health/ready` | anonymous |
