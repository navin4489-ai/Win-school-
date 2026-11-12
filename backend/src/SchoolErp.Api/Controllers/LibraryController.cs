using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Operations;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/library")]
[Authorize(Policy = "permission:library.manage")]
public sealed class LibraryController : ControllerBase
{
    private readonly OperationsService _ops;

    public LibraryController(OperationsService ops) => _ops = ops;

    [HttpGet("books")]
    public async Task<ActionResult<List<BookDto>>> Books([FromQuery] string? search, CancellationToken ct) =>
        Ok(await _ops.GetBooksAsync(search, ct));

    [HttpPost("books")]
    public async Task<ActionResult<BookDto>> CreateBook(SaveBookRequest request, CancellationToken ct) =>
        Ok(await _ops.CreateBookAsync(request, ct));

    [HttpPost("issues")]
    public async Task<ActionResult<BookTransactionDto>> Issue(IssueBookRequest request, CancellationToken ct) =>
        Ok(await _ops.IssueBookAsync(request, ct));

    [HttpPost("returns")]
    public async Task<ActionResult<BookTransactionDto>> Return(ReturnBookRequest request, CancellationToken ct) =>
        Ok(await _ops.ReturnBookAsync(request, ct));

    [HttpGet("transactions")]
    public async Task<ActionResult<List<BookTransactionDto>>> Transactions(CancellationToken ct) =>
        Ok(await _ops.GetBookTransactionsAsync(ct));
}
