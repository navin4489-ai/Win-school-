# BRD summary and traceability

Source: *School Management System — Complete School ERP*, Wintech Innovations, v1.0 (draft).
This page condenses the approved scope and maps it to the implementation.

## Objectives

Digitise admissions, records, fees, attendance, academics, HR, operations and communication; give
owners/principals real-time insight; give parents transparency; reduce paperwork; support multiple
schools and branches.

## Roles

Super Admin, School Owner, Principal, Administrator, Accountant, Admission Manager, Teacher, Class
Coordinator, Student, Parent, HR Manager, Librarian, Transport Manager, Transport Staff — all
permission-configurable.

## Functional scope → implementation

| BRD module | Backend | Web | Mobile | Phase |
| --- | --- | --- | --- | --- |
| 4.1 School administration | `AdministrationController`, `SchoolService` | Settings | — | 1 |
| 4.2 Admissions | `AdmissionsController`, `AdmissionService` | List + wizard | — | 2 |
| 4.3 SIS | `StudentsController`, `StudentService` | List + profile | Student home | 2 |
| 4.4 Parent portal | `ParentsController`, `ParentService` | Parent views | Parent home | 2/8 |
| 4.5 Fees | `FeesController`, `FeeService` | Collection + ledger | Fee summary | 3/8 |
| 4.6 Attendance | `AttendanceController`, `AttendanceService` | Marking sheet | Teacher marking | 4/8 |
| 4.7 Academics | Academic setup + `ClassSubjects` | Settings | — | 4 |
| 4.8 Timetable | `TimetableEntries`, `ExamsController` read | Timetable view | Timetable | 5/8 |
| 4.9 Exams/results | `ExamsController`, `ExamService` | Marks + reports | Results | 5/8 |
| 4.10 Homework | `HomeworkController` | Homework list | Homework | 5/8 |
| 4.11 LMS | `StudyMaterial` schema | Placeholder | — | 5+ |
| 4.12 Staff | `HrController` read | Staff directory | — | 6 |
| 4.13 HR/payroll | Payroll schema, run endpoints | Placeholder | — | 6 |
| 4.14 Transport | Read APIs, allocation schema | Placeholder | — | 7 |
| 4.15 Library | Read APIs, issue/return | Placeholder | — | 7 |
| 4.16 Inventory | Read APIs, stock schema | Placeholder | — | 7 |
| 4.17 Communication | Outbox + sender adapters | Notification centre | Push-ready | 8 |
| 4.18 Mobile | Flutter app | — | 4 role homes | 8 |
| 4.19 Dashboards | `DashboardController` | Role dashboards | Admin KPIs | 9 |
| 6 Finance | Ledger, expenses schema, summaries | Reports | — | 3/9 |
| 7 Documents | `IFileStorage`, metadata, certificates | Upload | — | 2 |
| 8 Reporting | CSV exports | Download buttons | — | 9 |
| 10 Security | Policies, tenancy, audit, lockout | Route guards | Token storage | 1/10 |
| 13 Integrations | Gateway/SMS/WhatsApp/email abstractions | — | — | 3/8 |

## Business rules coverage

| # | Rule | Where enforced |
| --- | --- | --- |
| 1 | Unique admission number per school | unique index + `StudentService` |
| 2 | Every student has session/class/section | entity invariants + enrollment |
| 3 | Fee structure per session/class | unique index + service guard |
| 4 | Unique receipt numbers | sequence + unique index |
| 5 | Online payments reconciled | `PaymentTransactions` + verify endpoint |
| 6 | Refund approval | `Refunds.ApprovedBy` required |
| 7 | Only assigned teachers mark attendance | `AttendanceService` scope check |
| 8 | Attendance changes audited | audit interceptor |
| 9 | Marks approved before results | `ExamService` status guard |
| 10 | Grading rules applied | `GradingCalculator` |
| 11 | Parents see only linked children | `ParentService` |
| 12 | Students cannot modify records | permissions |
| 13 | Promotion preserves history | new enrollment + old session retained |
| 14 | Closed sessions immutable | `AcademicSession.IsClosed` guard |
| 15 | Sensitive operations logged | audit interceptor |

## Out of scope (initial release)

Proctoring, facial-recognition attendance, full accounting replacement, hostel, canteen, hardware
procurement, third-party subscriptions, predictive AI. Reserved as future modules.

## Success criteria

Core modules operational; central records; reconciled fees; working attendance/academics; parent and
teacher portals; trustworthy dashboards; security and UAT signed off; production deployment and
knowledge transfer complete.
