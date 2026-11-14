using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Administration;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public sealed class AdministrationController : ControllerBase
{
    private readonly AdministrationService _admin;

    public AdministrationController(AdministrationService admin) => _admin = admin;

    // School profile
    [HttpGet("school")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SchoolDto>> GetSchool(CancellationToken ct) =>
        Ok(await _admin.GetSchoolAsync(ct));

    [HttpPut("school")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SchoolDto>> UpdateSchool(UpdateSchoolRequest request, CancellationToken ct) =>
        Ok(await _admin.UpdateSchoolAsync(request, ct));

    // Branches
    [HttpGet("branches")]
    public async Task<ActionResult<List<BranchDto>>> Branches(CancellationToken ct) =>
        Ok(await _admin.GetBranchesAsync(ct));

    [HttpPost("branches")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<BranchDto>> CreateBranch(SaveBranchRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveBranchAsync(null, request, ct));

    [HttpPut("branches/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<BranchDto>> UpdateBranch(int id, SaveBranchRequest request,
        CancellationToken ct) => Ok(await _admin.SaveBranchAsync(id, request, ct));

    // Academic sessions
    [HttpGet("sessions")]
    public async Task<ActionResult<List<AcademicSessionDto>>> Sessions(CancellationToken ct) =>
        Ok(await _admin.GetSessionsAsync(ct));

    [HttpPost("sessions")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<AcademicSessionDto>> CreateSession(SaveAcademicSessionRequest request,
        CancellationToken ct) => Ok(await _admin.SaveSessionAsync(null, request, ct));

    [HttpPut("sessions/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<AcademicSessionDto>> UpdateSession(int id, SaveAcademicSessionRequest request,
        CancellationToken ct) => Ok(await _admin.SaveSessionAsync(id, request, ct));

    [HttpPost("sessions/{id:int}/close")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<IActionResult> CloseSession(int id, CancellationToken ct)
    {
        await _admin.CloseSessionAsync(id, ct);
        return NoContent();
    }

    // Classes
    [HttpGet("classes")]
    public async Task<ActionResult<List<ClassDto>>> Classes(CancellationToken ct) =>
        Ok(await _admin.GetClassesAsync(ct));

    [HttpPost("classes")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<ClassDto>> CreateClass(SaveClassRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveClassAsync(null, request, ct));

    [HttpPut("classes/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<ClassDto>> UpdateClass(int id, SaveClassRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveClassAsync(id, request, ct));

    [HttpDelete("classes/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<IActionResult> DeleteClass(int id, CancellationToken ct)
    {
        await _admin.DeleteClassAsync(id, ct);
        return NoContent();
    }

    // Sections
    [HttpGet("classes/{classId:int}/sections")]
    public async Task<ActionResult<List<SectionDto>>> Sections(int classId, CancellationToken ct) =>
        Ok(await _admin.GetSectionsAsync(classId, ct));

    [HttpPost("classes/{classId:int}/sections")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SectionDto>> CreateSection(int classId, SaveSectionRequest request,
        CancellationToken ct) => Ok(await _admin.SaveSectionAsync(classId, null, request, ct));

    [HttpPut("classes/{classId:int}/sections/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SectionDto>> UpdateSection(int classId, int id, SaveSectionRequest request,
        CancellationToken ct) => Ok(await _admin.SaveSectionAsync(classId, id, request, ct));

    [HttpDelete("classes/{classId:int}/sections/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<IActionResult> DeleteSection(int classId, int id, CancellationToken ct)
    {
        await _admin.DeleteSectionAsync(classId, id, ct);
        return NoContent();
    }

    // Subjects
    [HttpGet("subjects")]
    public async Task<ActionResult<List<SubjectDto>>> Subjects(CancellationToken ct) =>
        Ok(await _admin.GetSubjectsAsync(ct));

    [HttpPost("subjects")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SubjectDto>> CreateSubject(SaveSubjectRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveSubjectAsync(null, request, ct));

    [HttpPut("subjects/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<SubjectDto>> UpdateSubject(int id, SaveSubjectRequest request,
        CancellationToken ct) => Ok(await _admin.SaveSubjectAsync(id, request, ct));

    [HttpDelete("subjects/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<IActionResult> DeleteSubject(int id, CancellationToken ct)
    {
        await _admin.DeleteSubjectAsync(id, ct);
        return NoContent();
    }

    // Class subjects
    [HttpGet("classes/{classId:int}/subjects")]
    public async Task<ActionResult<List<ClassSubjectDto>>> ClassSubjects(int classId, CancellationToken ct) =>
        Ok(await _admin.GetClassSubjectsAsync(classId, ct));

    [HttpPost("classes/{classId:int}/subjects")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<ClassSubjectDto>> CreateClassSubject(int classId,
        SaveClassSubjectRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveClassSubjectAsync(classId, null, request, ct));

    [HttpPut("classes/{classId:int}/subjects/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<ClassSubjectDto>> UpdateClassSubject(int classId, int id,
        SaveClassSubjectRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveClassSubjectAsync(classId, id, request, ct));

    // Roles and permissions
    [HttpGet("roles")]
    [Authorize(Policy = "permission:users.manage")]
    public async Task<ActionResult<List<RoleDto>>> Roles(CancellationToken ct) =>
        Ok(await _admin.GetRolesAsync(ct));

    [HttpGet("permissions")]
    [Authorize(Policy = "permission:users.manage")]
    public async Task<ActionResult<List<PermissionDto>>> Permissions(CancellationToken ct) =>
        Ok(await _admin.GetPermissionsAsync(ct));

    [HttpPut("roles/{roleId:int}/permissions")]
    [Authorize(Policy = "permission:users.manage")]
    public async Task<ActionResult<RoleDto>> UpdateRolePermissions(int roleId,
        UpdateRolePermissionsRequest request, CancellationToken ct) =>
        Ok(await _admin.UpdateRolePermissionsAsync(roleId, request.PermissionIds, ct));

    // Timetable
    [HttpGet("timetable")]
    public async Task<ActionResult<List<TimetableEntryDto>>> Timetable([FromQuery] int? classId,
        [FromQuery] int? sectionId, [FromQuery] int? employeeId, CancellationToken ct) =>
        Ok(await _admin.GetTimetableAsync(classId, sectionId, employeeId, ct));

    [HttpPost("timetable")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<TimetableEntryDto>> CreateTimetableEntry(SaveTimetableEntryRequest request,
        CancellationToken ct) => Ok(await _admin.SaveTimetableEntryAsync(null, request, ct));

    [HttpPut("timetable/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<TimetableEntryDto>> UpdateTimetableEntry(int id,
        SaveTimetableEntryRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveTimetableEntryAsync(id, request, ct));

    // Announcements
    [HttpGet("announcements")]
    public async Task<ActionResult<List<AnnouncementDto>>> Announcements(CancellationToken ct) =>
        Ok(await _admin.GetAnnouncementsAsync(ct));

    [HttpPost("announcements")]
    [Authorize(Policy = "permission:communication.manage")]
    public async Task<ActionResult<AnnouncementDto>> CreateAnnouncement(SaveAnnouncementRequest request,
        CancellationToken ct) => Ok(await _admin.CreateAnnouncementAsync(request, ct));

    [HttpPut("announcements/{id:int}")]
    [Authorize(Policy = "permission:communication.manage")]
    public async Task<ActionResult<AnnouncementDto>> UpdateAnnouncement(int id, SaveAnnouncementRequest request,
        CancellationToken ct) => Ok(await _admin.UpdateAnnouncementAsync(id, request, ct));

    [HttpDelete("announcements/{id:int}")]
    [Authorize(Policy = "permission:communication.manage")]
    public async Task<IActionResult> DeleteAnnouncement(int id, CancellationToken ct)
    {
        await _admin.DeleteAnnouncementAsync(id, ct);
        return NoContent();
    }

    // Teacher assignments — the principal decides who teaches which class and section.
    [HttpGet("teacher-assignments")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<List<TeacherAssignmentDto>>> TeacherAssignments([FromQuery] int? classId,
        [FromQuery] int? sectionId, CancellationToken ct) =>
        Ok(await _admin.GetTeacherAssignmentsAsync(classId, sectionId, ct));

    [HttpPost("teacher-assignments")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<TeacherAssignmentDto>> CreateTeacherAssignment(
        SaveTeacherAssignmentRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveTeacherAssignmentAsync(null, request, ct));

    [HttpPut("teacher-assignments/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<ActionResult<TeacherAssignmentDto>> UpdateTeacherAssignment(int id,
        SaveTeacherAssignmentRequest request, CancellationToken ct) =>
        Ok(await _admin.SaveTeacherAssignmentAsync(id, request, ct));

    [HttpDelete("teacher-assignments/{id:int}")]
    [Authorize(Policy = "permission:settings.manage")]
    public async Task<IActionResult> DeleteTeacherAssignment(int id, CancellationToken ct)
    {
        await _admin.DeleteTeacherAssignmentAsync(id, ct);
        return NoContent();
    }
}
