using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Common;
using SchoolErp.Application.Students;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/students")]
public sealed class StudentsController : ControllerBase
{
    private readonly StudentService _students;

    public StudentsController(StudentService students) => _students = students;

    [HttpGet]
    [Authorize(Policy = "permission:students.read")]
    public async Task<ActionResult<PagedResult<StudentListItemDto>>> List([FromQuery] StudentQuery query,
        CancellationToken ct) => Ok(await _students.ListAsync(query, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = "permission:students.read")]
    public async Task<ActionResult<StudentDetailDto>> Get(int id, CancellationToken ct) =>
        Ok(await _students.GetAsync(id, ct));

    [HttpPut("{id:int}")]
    [Authorize(Policy = "permission:students.write")]
    public async Task<ActionResult<StudentDetailDto>> Update(int id, UpdateStudentRequest request,
        CancellationToken ct) => Ok(await _students.UpdateAsync(id, request, ct));

    [HttpPost("{id:int}/promote")]
    [Authorize(Policy = "permission:students.write")]
    public async Task<ActionResult<StudentDetailDto>> Promote(int id, PromoteStudentRequest request,
        CancellationToken ct) => Ok(await _students.PromoteAsync(id, request, ct));

    [HttpPost("{id:int}/withdraw")]
    [Authorize(Policy = "permission:students.write")]
    public async Task<IActionResult> Withdraw(int id, WithdrawStudentRequest request, CancellationToken ct)
    {
        await _students.WithdrawAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/documents")]
    [Authorize(Policy = "permission:students.write")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<StudentDocumentDto>> UploadDocument(int id, IFormFile file,
        [FromForm] DocumentType documentType, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "A file is required." });

        await using var stream = file.OpenReadStream();
        var document = await _students.UploadDocumentAsync(id, stream, file.FileName, file.ContentType,
            file.Length, documentType, ct);
        return Ok(document);
    }

    [HttpPost("{id:int}/certificates/{type}")]
    [Authorize(Policy = "permission:students.read")]
    public async Task<IActionResult> Certificate(int id, string type, CancellationToken ct)
    {
        var certificate = await _students.GenerateCertificateAsync(id, type, ct);
        return File(Encoding.UTF8.GetBytes(certificate.Content), certificate.ContentType,
            certificate.FileName);
    }
}
