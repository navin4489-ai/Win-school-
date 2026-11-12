using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Parents;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/parent")]
[Authorize(Roles = "Parent")]
public sealed class ParentsController : ControllerBase
{
    private readonly ParentService _parents;

    public ParentsController(ParentService parents) => _parents = parents;

    [HttpGet("children")]
    public async Task<ActionResult<List<ChildDto>>> Children(CancellationToken ct) =>
        Ok(await _parents.GetChildrenAsync(ct));

    [HttpGet("children/{studentId:int}/overview")]
    public async Task<ActionResult<ChildOverviewDto>> Overview(int studentId, CancellationToken ct) =>
        Ok(await _parents.GetOverviewAsync(studentId, ct));

    [HttpGet("children/{studentId:int}/attendance")]
    public async Task<ActionResult<StudentAttendanceSummaryDto>> Attendance(int studentId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await _parents.GetAttendanceAsync(studentId, from, to, ct));

    [HttpGet("children/{studentId:int}/fees")]
    public async Task<ActionResult<FeeSummaryDto>> Fees(int studentId, CancellationToken ct) =>
        Ok(await _parents.GetFeesAsync(studentId, ct));

    [HttpGet("children/{studentId:int}/results")]
    public async Task<ActionResult<List<ResultSummaryDto>>> Results(int studentId, CancellationToken ct) =>
        Ok(await _parents.GetResultsAsync(studentId, ct));
}
