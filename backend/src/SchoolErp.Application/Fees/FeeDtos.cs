using System.ComponentModel.DataAnnotations;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Fees;

public sealed class FeeCategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool IsRecurring { get; init; }
}

public sealed class SaveFeeCategoryRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
}

public sealed class FeeStructureItemDto
{
    public int Id { get; init; }
    public int FeeCategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public FeeFrequency Frequency { get; init; }
    public int DueDay { get; init; }
}

public sealed class FeeStructureDto
{
    public int Id { get; init; }
    public int AcademicSessionId { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<FeeStructureItemDto> Items { get; init; } = Array.Empty<FeeStructureItemDto>();
}

public sealed class FeeStructureItemInput
{
    public int FeeCategoryId { get; set; }
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    public FeeFrequency Frequency { get; set; } = FeeFrequency.Annual;
    [Range(1, 31)] public int DueDay { get; set; } = 10;
}

public sealed class SaveFeeStructureRequest
{
    [Range(1, int.MaxValue)] public int AcademicSessionId { get; set; }
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MinLength(1)] public List<FeeStructureItemInput> Items { get; set; } = new();
}

public sealed class InstallmentInput
{
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
}

public sealed class AssignFeeRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    [Range(1, int.MaxValue)] public int FeeStructureId { get; set; }
    [Range(0, 10_000_000)] public decimal DiscountAmount { get; set; }
    public List<InstallmentInput>? Installments { get; set; }
}

public sealed class InstallmentDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal PenaltyAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal Balance { get; init; }
    public InstallmentStatus Status { get; init; }
    public bool IsOverdue { get; init; }
}

public sealed class PaymentDto
{
    public int Id { get; init; }
    public string ReceiptNo { get; init; } = string.Empty;
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public PaymentMode Mode { get; init; }
    public PaymentStatus Status { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public string? ReferenceNo { get; init; }
    public decimal RefundedAmount { get; init; }
}

public sealed class StudentLedgerDto
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AdmissionNo { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public decimal TotalBilled { get; init; }
    public decimal Discount { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal Balance { get; init; }
    public decimal Overdue { get; init; }
    public decimal PenaltyTotal { get; init; }
    public IReadOnlyList<InstallmentDto> Installments { get; init; } = Array.Empty<InstallmentDto>();
    public IReadOnlyList<PaymentDto> Payments { get; init; } = Array.Empty<PaymentDto>();
}

public sealed class RecordPaymentRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    public int? FeeInstallmentId { get; set; }
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    public PaymentMode Mode { get; set; } = PaymentMode.Cash;
    [MaxLength(100)] public string? ReferenceNo { get; set; }
    [MaxLength(100)] public string? IdempotencyKey { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    [MaxLength(100)] public string? GatewayOrderId { get; set; }
    [MaxLength(100)] public string? GatewayPaymentId { get; set; }
}

public sealed class RefundRequest
{
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class PaymentQuery : PageQuery
{
    public int? StudentId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public PaymentMode? Mode { get; set; }
}

public sealed class CollectionPointDto
{
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

public sealed class CollectionSummaryDto
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public decimal TotalCollected { get; init; }
    public int PaymentCount { get; init; }
    public decimal TotalRefunded { get; init; }
    public IReadOnlyList<CollectionPointDto> ByMode { get; init; } = Array.Empty<CollectionPointDto>();
    public IReadOnlyList<CollectionPointDto> Daily { get; init; } = Array.Empty<CollectionPointDto>();
}

public sealed class DefaulterDto
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AdmissionNo { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string? GuardianPhone { get; init; }
    public decimal Outstanding { get; init; }
    public DateOnly OldestDueDate { get; init; }
}

public sealed class ReminderResult
{
    public int Queued { get; init; }
    public int Skipped { get; init; }
}

public sealed class CreatePaymentOrderRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    public int? FeeInstallmentId { get; set; }
}

/// <summary>A payment session opened with the gateway, ready to be shown at checkout.</summary>
public sealed class PaymentOrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string PaymentId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public string CheckoutUrl { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public string Gateway { get; init; } = string.Empty;
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? InstallmentName { get; init; }
}

public sealed class OnlinePaymentRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    public int? FeeInstallmentId { get; set; }
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    [Required, MaxLength(100)] public string GatewayOrderId { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string GatewayPaymentId { get; set; } = string.Empty;
    [MaxLength(100)] public string? IdempotencyKey { get; set; }
}

public sealed class FeeDocumentDto
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "text/html; charset=utf-8";
    public string Html { get; init; } = string.Empty;
}
