# Entity relationship overview

The physical model contains 55 tables. `database/schema.sql` is authoritative; this diagram shows the
core relationships used by the Phase 1–4 use cases.

```mermaid
erDiagram
    Schools ||--o{ Branches : has
    Schools ||--o{ AcademicSessions : runs
    Schools ||--o{ SchoolClasses : defines
    Schools ||--o{ Employees : employs
    Schools ||--o{ UserAccounts : owns

    AcademicSessions ||--o{ SchoolClasses : contains
    SchoolClasses ||--o{ Sections : splits
    SchoolClasses ||--o{ ClassSubjects : offers

    Subjects ||--o{ ClassSubjects : taught
    Employees ||--o{ ClassSubjects : teacher
    Sections ||--o{ Students : enrolls
    SchoolClasses ||--o{ Students : classifies

    Students ||--o{ StudentParents : linked
    Guardians ||--o{ StudentParents : linked
    UserAccounts ||--o| Guardians : portal

    AcademicSessions ||--o{ Admissions : intake
    SchoolClasses ||--o{ Admissions : applies
    Admissions ||--o| Students : becomes
    Students ||--o{ StudentDocuments : owns
    Documents ||--o{ StudentDocuments : metadata

    SchoolClasses ||--o{ FeeStructures : priced
    AcademicSessions ||--o{ FeeStructures : valid
    FeeCategories ||--o{ FeeStructureItems : category
    FeeStructures ||--o{ FeeStructureItems : lines

    Students ||--o{ StudentFeeAssignments : billed
    FeeStructures ||--o{ StudentFeeAssignments : source
    StudentFeeAssignments ||--o{ FeeInstallments : schedule
    FeeInstallments ||--o{ Payments : paid_by
    Payments ||--o{ PaymentTransactions : attempts
    Payments ||--o{ Refunds : refunded

    Students ||--o{ StudentAttendances : daily
    Sections ||--o{ StudentAttendances : roster
    Employees ||--o{ EmployeeAttendances : daily

    AcademicSessions ||--o{ Examinations : schedules
    Examinations ||--o{ ExamSubjects : includes
    ExamSubjects ||--o{ StudentMarks : records
    Students ||--o{ StudentMarks : scores
    Students ||--o{ ReportCards : receives

    SchoolClasses ||--o{ TimetableEntries : slots
    Subjects ||--o{ TimetableEntries : subject
    Employees ||--o{ TimetableEntries : teacher

    Students ||--o{ Homework : assigned
    Homework ||--o{ AssignmentSubmissions : submitted
    Employees ||--o{ LeaveApplications : requests
    Employees ||--o{ Payslips : paid
    PayrollRuns ||--o{ Payslips : generates

    Vehicles ||--o{ TransportAllocations : serves
    TransportRoutes ||--o{ TransportStops : contains
    TransportRoutes ||--o{ TransportAllocations : assigns
    Students ||--o{ TransportAllocations : rider

    Books ||--o{ BookTransactions : circulates
    Students ||--o{ BookTransactions : borrows
    InventoryItems ||--o{ StockTransactions : moves
    Vendors ||--o{ PurchaseOrders : supplies
    PurchaseOrders ||--o{ PurchaseOrderItems : lines

    UserAccounts ||--o{ AuditLogs : acts
    UserAccounts ||--o{ Notifications : receives
```

## Conventions

- Every table except `Schools` carries `SchoolId`; most also carry nullable `BranchId`.
- `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy`, `IsDeleted` exist on all business tables.
- `RowVersion` provides optimistic concurrency on mutable aggregates.
- Unique indexes are school-scoped, e.g. `(SchoolId, AdmissionNo)` and `(SchoolId, ReceiptNo)`.
- Money is `decimal(18,2)`; percentages `decimal(5,2)`; dates are `date`, timestamps `datetime2`.
- Ledger tables (`Payments`, `Refunds`, `AuditLogs`, `StockTransactions`) are append-only.

## Aggregate roots

| Aggregate | Root | Children |
| --- | --- | --- |
| Organisation | `School` | `Branch`, `AcademicSession`, `SystemConfiguration` |
| Academic structure | `SchoolClass` | `Section`, `ClassSubject` |
| Person | `Student` / `Employee` | documents, guardians, attendance, marks |
| Admission | `Admission` | documents, interview notes |
| Billing | `StudentFeeAssignment` | `FeeInstallment`, `Payment`, `Refund` |
| Assessment | `Examination` | `ExamSubject`, `StudentMark`, `ReportCard` |
| Operations | `Vehicle` / `Book` / `InventoryItem` | allocations, transactions |
