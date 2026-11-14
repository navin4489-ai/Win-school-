using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Common;
using SchoolErp.Application.Fees;
using SchoolErp.Domain.Entities;
using System.Text;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/fees")]
public sealed class FeesController : ControllerBase
{
    private readonly FeeService _fees;

    public FeesController(FeeService fees) => _fees = fees;

    [HttpGet("categories")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<List<FeeCategoryDto>>> Categories(CancellationToken ct) =>
        Ok(await _fees.GetCategoriesAsync(ct));

    [HttpPost("categories")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeCategoryDto>> CreateCategory(SaveFeeCategoryRequest request,
        CancellationToken ct) => Ok(await _fees.CreateCategoryAsync(request, ct));

    [HttpPut("categories/{id:int}")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeCategoryDto>> UpdateCategory(int id, SaveFeeCategoryRequest request,
        CancellationToken ct) => Ok(await _fees.UpdateCategoryAsync(id, request, ct));

    [HttpDelete("categories/{id:int}")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        await _fees.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpGet("structures")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<List<FeeStructureDto>>> Structures([FromQuery] int? sessionId,
        [FromQuery] int? classId, CancellationToken ct) =>
        Ok(await _fees.GetStructuresAsync(sessionId, classId, ct));

    [HttpPost("structures")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeStructureDto>> CreateStructure(SaveFeeStructureRequest request,
        CancellationToken ct) => Ok(await _fees.CreateStructureAsync(request, ct));

    [HttpPut("structures/{id:int}")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeStructureDto>> UpdateStructure(int id, SaveFeeStructureRequest request,
        CancellationToken ct) => Ok(await _fees.UpdateStructureAsync(id, request, ct));

    [HttpPut("structures/{id:int}/active")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeStructureDto>> SetStructureActive(int id, [FromQuery] bool active,
        CancellationToken ct) => Ok(await _fees.SetStructureActiveAsync(id, active, ct));

    [HttpDelete("structures/{id:int}")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<IActionResult> DeleteStructure(int id, CancellationToken ct)
    {
        await _fees.DeleteStructureAsync(id, ct);
        return NoContent();
    }

    [HttpPost("assignments/{id:int}/cancel")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<StudentLedgerDto>> CancelAssignment(int id, FeeReasonRequest request,
        CancellationToken ct) => Ok(await _fees.CancelAssignmentAsync(id, request, ct));

    [HttpPost("assignments/{id:int}/discount")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<StudentLedgerDto>> AddDiscount(int id, FeeDiscountRequest request,
        CancellationToken ct) => Ok(await _fees.AddDiscountAsync(id, request, ct));

    [HttpPost("installments/{id:int}/waive")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<StudentLedgerDto>> WaiveInstallment(int id, FeeReasonRequest request,
        CancellationToken ct) => Ok(await _fees.WaiveInstallmentAsync(id, request, ct));

    [HttpPut("installments/{id:int}")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<StudentLedgerDto>> UpdateInstallment(int id, UpdateInstallmentRequest request,
        CancellationToken ct) => Ok(await _fees.UpdateInstallmentAsync(id, request, ct));

    [HttpPost("assignments")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<StudentLedgerDto>> Assign(AssignFeeRequest request, CancellationToken ct) =>
        Ok(await _fees.AssignAsync(request, ct));

    [HttpGet("students/{studentId:int}/ledger")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<StudentLedgerDto>> Ledger(int studentId, CancellationToken ct) =>
        Ok(await _fees.GetLedgerAsync(studentId, ct));

    [HttpPost("payments")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<PaymentDto>> RecordPayment(RecordPaymentRequest request, CancellationToken ct) =>
        Ok(await _fees.RecordPaymentAsync(request, ct));

    /// <summary>Whether online payment is available (a real gateway is configured).</summary>
    [HttpGet("payment-config")]
    [Authorize(Policy = "permission:fees.read")]
    public IActionResult PaymentConfig() => Ok(_fees.GetPaymentConfig());

    /// <summary>Opens a payment session at the gateway. Parents may do this for their own children.</summary>
    [HttpPost("payments/order")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<PaymentOrderDto>> CreateOrder(CreatePaymentOrderRequest request,
        CancellationToken ct) => Ok(await _fees.CreatePaymentOrderAsync(request, ct));

    /// <summary>Completes an online payment. Parents may do this for their own children.</summary>
    [HttpPost("payments/online")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<PaymentDto>> CompleteOnlinePayment(OnlinePaymentRequest request,
        CancellationToken ct) => Ok(await _fees.RecordOnlinePaymentAsync(request, ct));

    /// <summary>Printable receipt for a payment.</summary>
    [HttpGet("payments/{paymentId:int}/receipt")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<IActionResult> Receipt(int paymentId, CancellationToken ct)
    {
        var document = await _fees.GetReceiptAsync(paymentId, ct);
        return File(document.Content, document.ContentType, document.FileName);
    }

    /// <summary>Printable fee statement for a student.</summary>
    [HttpGet("students/{studentId:int}/statement")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<IActionResult> Statement(int studentId, CancellationToken ct)
    {
        var document = await _fees.GetStatementAsync(studentId, ct);
        return File(document.Content, document.ContentType, document.FileName);
    }

    [HttpGet("payments")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<PagedResult<PaymentDto>>> Payments([FromQuery] PaymentQuery query,
        CancellationToken ct) => Ok(await _fees.GetPaymentsAsync(query, ct));

    [HttpPost("payments/{paymentId:int}/refunds")]
    [Authorize(Policy = "permission:fees.refund")]
    public async Task<ActionResult<Refund>> RequestRefund(int paymentId, RefundRequest request,
        CancellationToken ct) => Ok(await _fees.RequestRefundAsync(paymentId, request, ct));

    [HttpPost("refunds/{refundId:int}/approve")]
    [Authorize(Policy = "permission:fees.refund")]
    public async Task<IActionResult> ApproveRefund(int refundId, CancellationToken ct)
    {
        await _fees.ApproveRefundAsync(refundId, ct);
        return NoContent();
    }

    [HttpGet("summary")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<CollectionSummaryDto>> Summary([FromQuery] DateOnly from,
        [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await _fees.GetCollectionSummaryAsync(from, to, ct));

    [HttpGet("outstanding")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<List<DefaulterDto>>> Outstanding([FromQuery] int? classId,
        CancellationToken ct) => Ok(await _fees.GetOutstandingAsync(classId, ct));

    [HttpPost("reminders")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<ReminderResult>> Reminders(CancellationToken ct) =>
        Ok(await _fees.QueueFeeRemindersAsync(ct));
}
