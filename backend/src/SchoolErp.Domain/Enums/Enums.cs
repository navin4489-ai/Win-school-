namespace SchoolErp.Domain.Enums;

public enum Gender { Male, Female, Other }

public enum StudentStatus { Active, Withdrawn, Alumni, Transferred }

public enum AdmissionStatus
{
    Enquiry,
    Application,
    DocumentsVerified,
    InterviewScheduled,
    Approved,
    Rejected,
    Waitlisted,
    Enrolled,
    Cancelled
}

public enum AttendanceStatus { Present, Absent, Late, HalfDay, Leave }

public enum AttendanceSource { Manual, Biometric, Rfid, Qr, Gps, Mobile }

public enum EmploymentStatus { Active, OnLeave, Resigned, Terminated, Retired }

public enum PaymentMode { Cash, Upi, Card, NetBanking, Cheque, BankTransfer, Online }

public enum PaymentStatus { Pending, Success, Failed, Refunded, PartiallyRefunded }

public enum InstallmentStatus { Pending, PartiallyPaid, Paid, Overdue, Waived }

public enum PaymentTransactionStatus { Initiated, Success, Failed }

public enum RefundStatus { Pending, Approved, Rejected, Processed }

public enum DiscountType { Percentage, FixedAmount }

public enum FeeFrequency { Monthly, Quarterly, HalfYearly, Annual, OneTime }

public enum NotificationChannel { InApp, Sms, Email, WhatsApp, Push }

public enum NotificationStatus { Pending, Sent, Failed, Read }

public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }

public enum ExamType { UnitTest, MonthlyTest, HalfYearly, Annual, Practical, InternalAssessment }

public enum ExamStatus { Draft, MarksEntry, PendingApproval, Approved, Published, Closed }

public enum DocumentType
{
    BirthCertificate,
    AddressProof,
    Photograph,
    TransferCertificate,
    Marksheet,
    IdProof,
    MedicalRecord,
    EmployeeContract,
    Other
}

public enum BookTransactionType { Issue, Return, Renew, Lost }

public enum StockTransactionType { Inward, Outward, Adjustment, Return }

public enum SalaryComponentType { Earning, Deduction }

public enum PayrollStatus { Draft, Processing, Processed, Paid, Cancelled }
