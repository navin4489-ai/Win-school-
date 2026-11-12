using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Common;
using SchoolErp.Application.Fees;
using SchoolErp.Domain.Entities;

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

    [HttpGet("structures")]
    [Authorize(Policy = "permission:fees.read")]
    public async Task<ActionResult<List<FeeStructureDto>>> Structures([FromQuery] int? sessionId,
        [FromQuery] int? classId, CancellationToken ct) =>
        Ok(await _fees.GetStructuresAsync(sessionId, classId, ct));

    [HttpPost("structures")]
    [Authorize(Policy = "permission:fees.collect")]
    public async Task<ActionResult<FeeStructureDto>> CreateStructure(SaveFeeStructureRequest request,
        CancellationToken ct) => Ok(await _fees.CreateStructureAsync(request, ct));

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
