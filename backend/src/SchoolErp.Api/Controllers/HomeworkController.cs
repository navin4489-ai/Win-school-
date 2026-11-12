using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Homework;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/homework")]
[Authorize]
public sealed class HomeworkController : ControllerBase
{
    private readonly HomeworkService _homework;

    public HomeworkController(HomeworkService homework) => _homework = homework;

    [HttpGet]
    public async Task<ActionResult<List<HomeworkDto>>> List([FromQuery] int? classId,
        [FromQuery] int? sectionId, [FromQuery] int? subjectId, CancellationToken ct) =>
        Ok(await _homework.ListAsync(classId, sectionId, subjectId, ct));

    [HttpPost]
    [Authorize(Policy = "permission:homework.manage")]
    public async Task<ActionResult<HomeworkDto>> Create(SaveHomeworkRequest request, CancellationToken ct) =>
        Ok(await _homework.CreateAsync(request, ct));

    [HttpGet("{homeworkId:int}/submissions")]
    [Authorize(Policy = "permission:homework.manage")]
    public async Task<ActionResult<List<SubmissionDto>>> Submissions(int homeworkId, CancellationToken ct) =>
        Ok(await _homework.GetSubmissionsAsync(homeworkId, ct));

    [HttpPost("{homeworkId:int}/submissions")]
    public async Task<ActionResult<SubmissionDto>> Submit(int homeworkId, SubmitHomeworkRequest request,
        CancellationToken ct) => Ok(await _homework.SubmitAsync(homeworkId, request, ct));

    [HttpPost("submissions/{submissionId:int}/grade")]
    [Authorize(Policy = "permission:homework.manage")]
    public async Task<ActionResult<SubmissionDto>> Grade(int submissionId, GradeSubmissionRequest request,
        CancellationToken ct) => Ok(await _homework.GradeAsync(submissionId, request, ct));
}
