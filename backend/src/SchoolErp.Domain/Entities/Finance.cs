using SchoolErp.Domain.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Domain.Entities;

public class FeeCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeeStructure : AuditableEntity
{
    public int AcademicSessionId { get; set; }
    public int ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeeStructureItem : AuditableEntity
{
    public int FeeStructureId { get; set; }
    public int FeeCategoryId { get; set; }
    public decimal Amount { get; set; }
    public FeeFrequency Frequency { get; set; } = FeeFrequency.Annual;
    public int DueDay { get; set; } = 10;
}

public class StudentFeeAssignment : AuditableEntity
{
    public int StudentId { get; set; }
    public int FeeStructureId { get; set; }
    public int AcademicSessionId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class FeeInstallment : AuditableEntity
{
    public int StudentFeeAssignmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    /// <summary>Late fee accrued by the penalty job for an unpaid installment.</summary>
    public decimal PenaltyAmount { get; set; }
    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalAmount => Amount + PenaltyAmount;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal Balance => Math.Max(0, TotalAmount - PaidAmount);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsOverdue => Status != InstallmentStatus.Paid && DueDate < DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>
/// One late-fee row per installment per day. Append only, so re-running the penalty
/// job cannot double charge a day (see ADR 0004 for the same append-only rule applied
/// to payments).
/// </summary>
public class FeePenalty : AuditableEntity
{
    public int StudentFeeAssignmentId { get; set; }
    public int FeeInstallmentId { get; set; }
    public int StudentId { get; set; }
    public DateOnly PenaltyDate { get; set; }
    public decimal Amount { get; set; }
    public int DaysLate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
}

public class Payment : AuditableEntity
{
    public string ReceiptNo { get; set; } = string.Empty;
    public int StudentId { get; set; }
    public int? StudentFeeAssignmentId { get; set; }
    public int? FeeInstallmentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode Mode { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Success;
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReferenceNo { get; set; }
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public string? IdempotencyKey { get; set; }
    public int? CollectedBy { get; set; }
    public string? Remarks { get; set; }
    public decimal RefundedAmount { get; set; }
}

public class PaymentTransaction : AuditableEntity
{
    public int? PaymentId { get; set; }
    public int StudentId { get; set; }
    public string Gateway { get; set; } = "Manual";
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Initiated;
    public string? RawResponse { get; set; }
    public string? FailureReason { get; set; }
}

public class Discount : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; } = DiscountType.Percentage;
    public decimal Value { get; set; }
    public string? AppliesTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StudentDiscount : AuditableEntity
{
    public int StudentId { get; set; }
    public int DiscountId { get; set; }
    public int AcademicSessionId { get; set; }
    public decimal Amount { get; set; }
    public int? ApprovedBy { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Refund : AuditableEntity
{
    public int PaymentId { get; set; }
    public int StudentId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public int RequestedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ReferenceNo { get; set; }
}

public class ExpenseCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class Expense : AuditableEntity
{
    public int ExpenseCategoryId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode Mode { get; set; } = PaymentMode.Cash;
    public string? VendorName { get; set; }
    public string? Description { get; set; }
    public string? VoucherNo { get; set; }
    public int? RecordedBy { get; set; }
}

public class SalaryComponent : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; } = SalaryComponentType.Earning;
    public bool IsTaxable { get; set; }
    public bool IsStatutory { get; set; }
}

public class EmployeeSalaryComponent : AuditableEntity
{
    public int EmployeeId { get; set; }
    public int SalaryComponentId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public class PayrollRun : AuditableEntity
{
    public int Month { get; set; }
    public int Year { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public DateTime? ProcessedAtUtc { get; set; }
    public int? ProcessedBy { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }
}

public class Payslip : AuditableEntity
{
    public int PayrollRunId { get; set; }
    public int EmployeeId { get; set; }
    public decimal Basic { get; set; }
    public decimal Gross { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    public int DaysPresent { get; set; }
    public int DaysAbsent { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
