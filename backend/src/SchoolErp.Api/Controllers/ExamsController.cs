using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Exams;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/exams")]
public sealed class ExamsController : ControllerBase
{
    private readonly ExamService _exams;

    public ExamsController(ExamService exams) => _exams = exams;

    [HttpGet]
    [Authorize(Policy = "permission:exams.read")]
    public async Task<ActionResult<List<ExamDto>>> List([FromQuery] int? sessionId, CancellationToken ct) =>
        Ok(await _exams.ListAsync(sessionId, ct));

    [HttpPost]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<ActionResult<ExamDto>> Create(SaveExamRequest request, CancellationToken ct) =>
        Ok(await _exams.CreateAsync(request, ct));

    [HttpPut("{examId:int}")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<ActionResult<ExamDto>> Update(int examId, UpdateExamRequest request, CancellationToken ct) =>
        Ok(await _exams.UpdateAsync(examId, request, ct));

    [HttpDelete("{examId:int}")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<IActionResult> Delete(int examId, CancellationToken ct)
    {
        await _exams.DeleteAsync(examId, ct);
        return NoContent();
    }

    [HttpGet("{examId:int}/subjects")]
    [Authorize(Policy = "permission:exams.read")]
    public async Task<ActionResult<List<ExamSubjectDto>>> Subjects(int examId, CancellationToken ct) =>
        Ok(await _exams.GetSubjectsAsync(examId, ct));

    [HttpPost("{examId:int}/subjects")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<ActionResult<ExamSubjectDto>> AddSubject(int examId, SaveExamSubjectRequest request,
        CancellationToken ct) => Ok(await _exams.AddSubjectAsync(examId, request, ct));

    [HttpGet("subjects/{examSubjectId:int}/marks")]
    [Authorize(Policy = "permission:exams.read")]
    public async Task<ActionResult<List<StudentMarkDto>>> Marks(int examSubjectId, CancellationToken ct) =>
        Ok(await _exams.GetMarksAsync(examSubjectId, ct));

    [HttpPost("{examId:int}/marks")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<ActionResult<int>> SaveMarks(int examId, SaveMarksRequest request, CancellationToken ct) =>
        Ok(new { saved = await _exams.SaveMarksAsync(examId, request, ct) });

    [HttpPost("{examId:int}/submit")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<IActionResult> SubmitForApproval(int examId, CancellationToken ct)
    {
        await _exams.SubmitForApprovalAsync(examId, ct);
        return NoContent();
    }

    [HttpPost("{examId:int}/approve")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<IActionResult> Approve(int examId, CancellationToken ct)
    {
        await _exams.ApproveAsync(examId, ct);
        return NoContent();
    }

    [HttpPost("{examId:int}/publish")]
    [Authorize(Policy = "permission:exams.manage")]
    public async Task<ActionResult<int>> Publish(int examId, CancellationToken ct) =>
        Ok(new { reportCards = await _exams.PublishAsync(examId, ct) });

    [HttpGet("{examId:int}/results")]
    [Authorize(Policy = "permission:exams.read")]
    public async Task<ActionResult<List<ExamResultRowDto>>> Results(int examId, [FromQuery] int? classId,
        CancellationToken ct) => Ok(await _exams.GetResultsAsync(examId, classId, ct));

    [HttpGet("{examId:int}/report-cards/{studentId:int}")]
    [Authorize(Policy = "permission:exams.read")]
    public async Task<ActionResult<ReportCardDto>> ReportCard(int examId, int studentId,
        CancellationToken ct) => Ok(await _exams.GetReportCardAsync(examId, studentId, ct));
}
