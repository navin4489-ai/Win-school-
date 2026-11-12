using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Reports;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = "permission:reports.read")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports) => _reports = reports;

    [HttpGet("types")]
    public ActionResult<IReadOnlyList<ReportTypeDto>> Types() => Ok(ReportService.Types);

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string type, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int? classId, [FromQuery] int? sessionId,
        CancellationToken ct)
    {
        var file = await _reports.ExportAsync(type, from, to, classId, sessionId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
