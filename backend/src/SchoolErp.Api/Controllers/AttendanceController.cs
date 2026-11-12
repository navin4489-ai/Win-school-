using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Attendance;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    private readonly AttendanceService _attendance;

    public AttendanceController(AttendanceService attendance) => _attendance = attendance;

    [HttpGet("roster")]
    [Authorize(Policy = "permission:attendance.read")]
    public async Task<ActionResult<ClassAttendanceDto>> Roster([FromQuery] int classId,
        [FromQuery] int sectionId, [FromQuery] DateOnly date, CancellationToken ct) =>
        Ok(await _attendance.GetClassRosterAsync(classId, sectionId, date, ct));

    [HttpPost("mark")]
    [Authorize(Policy = "permission:attendance.mark")]
    public async Task<ActionResult<MarkAttendanceResult>> Mark(MarkAttendanceRequest request,
        CancellationToken ct) => Ok(await _attendance.MarkBulkAsync(request, ct));

    [HttpGet("students/{studentId:int}")]
    [Authorize(Policy = "permission:attendance.read")]
    public async Task<ActionResult<StudentAttendanceSummaryDto>> StudentSummary(int studentId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await _attendance.GetStudentSummaryAsync(studentId, from, to, ct));

    [HttpGet("staff")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<StaffAttendanceDto>>> Staff([FromQuery] DateOnly date,
        CancellationToken ct) => Ok(await _attendance.GetStaffAttendanceAsync(date, ct));

    [HttpPost("staff/check-in")]
    [Authorize(Policy = "permission:attendance.mark")]
    public async Task<ActionResult<StaffAttendanceDto>> CheckIn(CheckInRequest request, CancellationToken ct) =>
        Ok(await _attendance.CheckInAsync(request, ct));

    [HttpPost("staff/check-out")]
    [Authorize(Policy = "permission:attendance.mark")]
    public async Task<ActionResult<StaffAttendanceDto>> CheckOut(CheckInRequest request, CancellationToken ct) =>
        Ok(await _attendance.CheckOutAsync(request, ct));
}
