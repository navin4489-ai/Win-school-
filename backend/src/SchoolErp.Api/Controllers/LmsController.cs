using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Operations;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/lms")]
[Authorize]
public sealed class LmsController : ControllerBase
{
    private readonly OperationsService _ops;

    public LmsController(OperationsService ops) => _ops = ops;

    [HttpGet("materials")]
    public async Task<ActionResult<List<StudyMaterialDto>>> Materials([FromQuery] int? classId,
        [FromQuery] int? subjectId, CancellationToken ct) =>
        Ok(await _ops.GetMaterialsAsync(classId, subjectId, ct));

    [HttpPost("materials")]
    [Authorize(Policy = "permission:lms.manage")]
    public async Task<ActionResult<StudyMaterialDto>> Create(SaveStudyMaterialRequest request,
        CancellationToken ct) => Ok(await _ops.CreateMaterialAsync(request, ct));
}
