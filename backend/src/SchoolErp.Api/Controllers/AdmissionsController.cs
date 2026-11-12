using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Admissions;
using SchoolErp.Application.Common;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/admissions")]
[Authorize(Policy = "permission:admissions.manage")]
public sealed class AdmissionsController : ControllerBase
{
    private readonly AdmissionService _admissions;

    public AdmissionsController(AdmissionService admissions) => _admissions = admissions;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdmissionDto>>> List([FromQuery] AdmissionQuery query,
        CancellationToken ct) => Ok(await _admissions.ListAsync(query, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdmissionDto>> Get(int id, CancellationToken ct) =>
        Ok(await _admissions.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<AdmissionDto>> Create(CreateEnquiryRequest request, CancellationToken ct)
    {
        var admission = await _admissions.CreateEnquiryAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = admission.Id }, admission);
    }

    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<AdmissionDto>> Submit(int id, CancellationToken ct) =>
        Ok(await _admissions.SubmitAsync(id, ct));

    [HttpPost("{id:int}/verify-documents")]
    public async Task<ActionResult<AdmissionDto>> VerifyDocuments(int id, CancellationToken ct) =>
        Ok(await _admissions.VerifyDocumentsAsync(id, ct));

    [HttpPost("{id:int}/schedule-interview")]
    public async Task<ActionResult<AdmissionDto>> ScheduleInterview(int id, ScheduleInterviewRequest request,
        CancellationToken ct) => Ok(await _admissions.ScheduleInterviewAsync(id, request, ct));

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<AdmissionDto>> Approve(int id, CancellationToken ct) =>
        Ok(await _admissions.ApproveAsync(id, ct));

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<AdmissionDto>> Reject(int id, RejectAdmissionRequest request,
        CancellationToken ct) => Ok(await _admissions.RejectAsync(id, request, ct));

    [HttpPost("{id:int}/waitlist")]
    public async Task<ActionResult<AdmissionDto>> Waitlist(int id, CancellationToken ct) =>
        Ok(await _admissions.WaitlistAsync(id, ct));

    [HttpPost("{id:int}/enroll")]
    public async Task<ActionResult<EnrollmentResult>> Enroll(int id, EnrollAdmissionRequest request,
        CancellationToken ct) => Ok(await _admissions.EnrollAsync(id, request, ct));
}
