# Delivery roadmap and backlog

The BRD's 27-week plan is preserved. This repository front-loads Phases 1–4 because every later
module depends on school setup, identity and the student/fee core.

## Phase status

| Phase | Weeks | Delivered in repository | Remaining before sign-off |
| --- | --- | --- | --- |
| 1 Foundation | 2 | Solution, EF model, JWT, refresh rotation, permission policies, school/branch/session/class/subject CRUD, audit trail, seeding | MFA enrolment UI, tenant onboarding wizard |
| 2 Admission & SIS | 3 | Enquiry → application → approve → enroll, guardians, documents metadata, promotion, withdrawal, certificates (HTML) | Interview scheduling UI, bulk import, DigiLocker |
| 3 Fees | 3 | Structures, assignment, installments, payments, receipts, refunds, ledger, reminders, CSV reports | Razorpay/Cashfree live adapter, reconciliation console |
| 4 Attendance & academics | 3 | Student/staff attendance, bulk marking, summaries, correction audit, timetable CRUD | Biometric/RFID device adapters, QR check-in |
| 5 Exams, homework | 3 | Exam setup, marks entry/approval, grading, ranks, report cards, homework + submissions | Online submission upload UI, grade-card templates per board |
| 6 HR & payroll | 2 | Employee directory, departments, designations, leave workflow, salary components, payroll runs, payslips | PF/ESI/professional-tax rule engine, loans/advances, F&F |
| 7 Transport/library/inventory | 3 | Schema, allocations, issue/return, stock movements, read APIs | GPS ingestion, fine automation, purchase approval workflow |
| 8 Mobile & notifications | 4 | Flutter shell with role homes, notifications outbox, SMS/email/WhatsApp adapters | Push (FCM/APNs) credentials, deep links, offline cache |
| 9 Dashboards/reports | 2 | Role dashboards, CSV exports, collection/attendance/performance aggregates | PDF report designer, scheduled email reports |
| 10 Hardening/UAT | 2 | Threat model, security checklist, UAT script, CI pipeline, deployment runbook | Pen test, load test at contracted concurrency, DR drill |

## Module backlog (epics)

1. **Configuration** — academic session rollover, holiday calendar, grading scales, document templates.
2. **Admissions** — enquiry source tracking, waitlist automation, interview slots, fee-linked approval.
3. **SIS** — custom fields, sibling linking, alumni, medical access restrictions, bulk promotion.
4. **Fees** — fine accrual job, sibling concession rules, gateway webhooks, reconciliation, Tally export.
5. **Attendance** — device ingestion, anomaly detection, QR/RFID, staff GPS (opt-in).
6. **Academics** — lesson plans, syllabus coverage, NEP record templates, substitution engine.
7. **Examinations** — practical/internal split, moderation, grace marks workflow, rank opt-in.
8. **Homework/LMS** — quizzes, SCORM content, video hosting, progress tracking.
9. **HR/Payroll** — statutory rules, loan ledger, attendance-linked pay, payslip PDF.
10. **Operations** — transport GPS, library fines, purchase approvals, stock valuation.
11. **Communication** — templates, delivery retries, parent-teacher threads, emergency broadcast.
12. **Mobile** — offline attendance, biometric login, push, fee payment SDK.
13. **Analytics** — warehouse, branch benchmarking, cohort retention, fee forecasting.
14. **Platform** — multi-school SaaS billing, branding per school, self-service onboarding.

## Definition of done (every epic)

- Permission constants + policy on every endpoint; scope checks covered by tests.
- Audit entries for mutations; notifications via outbox.
- OpenAPI updated; Angular/Flutter client wired or explicitly deferred.
- Unit tests for calculations and authorization; integration test for the happy path.
- Migration script, rollback notes and seed updates.
- UAT scenario added to `docs/uat-checklist.md`.
