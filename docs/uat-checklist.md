# UAT checklist

Execute on staging with production-like data. Record evidence (screenshot, request id) per scenario.

## Foundation and security

- [ ] Super Admin configures school profile, logo, address and contact details.
- [ ] Academic session created and set current; second session cannot be edited after closure.
- [ ] Classes, sections, subjects and streams created; duplicate class name rejected.
- [ ] Role permission matrix edited; affected user sees the change after token refresh.
- [ ] Login with wrong password 5× locks the account for 15 minutes.
- [ ] Refresh token rotation works; replay of an old refresh token is rejected.
- [ ] User from School A cannot read or update any School B record (expect 404).
- [ ] Teacher cannot mark attendance for an unassigned class.
- [ ] Parent cannot open another parent's child by changing the id.
- [ ] Audit log shows before/after values for a fee edit and an attendance correction.
- [ ] Rate limit returns 429 after 10 login attempts per minute.

## Admission and SIS

- [ ] Enquiry captured; application number generated and unique.
- [ ] Documents uploaded (allowed types) and rejected (exe/pdf > 10 MB).
- [ ] Verification → approval → enrollment creates student, guardian link and parent login.
- [ ] Duplicate admission number or double enrollment is blocked.
- [ ] Student profile shows class, section, guardians, documents and history.
- [ ] Promotion to next session keeps prior session results intact.
- [ ] Transfer/character/bonafide certificate generates with correct details.
- [ ] Withdrawal moves the student to alumni and blocks further fee assignment.

## Fees

- [ ] Class/session fee structure with multiple heads and installments.
- [ ] Student assignment generates expected installment schedule.
- [ ] Partial payment updates balance and ledger correctly.
- [ ] Receipt number unique; duplicate idempotency key returns the same receipt.
- [ ] Failed gateway payment does not change the balance.
- [ ] Refund requires approval and appears in the ledger and refund report.
- [ ] Outstanding/defaulter report totals reconcile with the ledger.
- [ ] Parent pays online and sees the receipt immediately.

## Attendance

- [ ] Bulk marking for a class persists present/absent/late/half-day.
- [ ] Absent notification is queued once and delivered once.
- [ ] Correction after submission is audited and reflected in monthly summary.
- [ ] Staff check-in/out and late tracking work; monthly summary matches raw entries.
- [ ] Attendance percentage correct for a student with mixed statuses.

## Exams and academics

- [ ] Exam with theory/practical max/min marks validates total boundaries.
- [ ] Marks entry above maximum is rejected; bulk upload reports row errors.
- [ ] Approval freezes marks; reopen is audited.
- [ ] Totals, percentage, grade, pass/fail and rank match the configured grading scale.
- [ ] Report card PDF/HTML matches marks and student identity.
- [ ] Parent notification sent on publication.
- [ ] Timetable conflict detection blocks two classes for the same teacher/room.

## HR, operations, reporting

- [ ] Employee onboarding, department/designation assignment and document storage.
- [ ] Leave application → approval → balance update.
- [ ] Payroll run computes gross, deductions, net; payslip matches.
- [ ] Library issue/return, overdue fine and lost-book handling.
- [ ] Inventory goods receipt increases stock; issue decreases it; minimum alert fires.
- [ ] Transport allocation and route/stop assignment; expiry alerts for insurance/fitness.
- [ ] All 12 report families export correct filtered PDF/Excel/CSV.
- [ ] Dashboards reconcile with underlying reports for the same date range.

## Mobile

- [ ] Parent, teacher, student and admin logins land on the correct home.
- [ ] Teacher marks attendance on Android and iOS.
- [ ] Parent views fees and pays; student views timetable and results.
- [ ] Push/notification opens the relevant record.
- [ ] Token expiry silently refreshes; logout clears local data.

## Sign-off

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Principal | | | |
| Accountant | | | |
| IT / Security | | | |
| Wintech PM | | | |
