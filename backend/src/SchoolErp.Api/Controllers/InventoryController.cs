using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Operations;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize(Policy = "permission:inventory.manage")]
public sealed class InventoryController : ControllerBase
{
    private readonly OperationsService _ops;

    public InventoryController(OperationsService ops) => _ops = ops;

    [HttpGet]
    public async Task<ActionResult<List<InventoryItemDto>>> Items([FromQuery] string? search,
        CancellationToken ct) => Ok(await _ops.GetInventoryAsync(search, ct));

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(SaveInventoryItemRequest request,
        CancellationToken ct) => Ok(await _ops.CreateInventoryItemAsync(request, ct));

    [HttpPost("stock")]
    public async Task<ActionResult<InventoryItemDto>> Stock(StockEntryRequest request, CancellationToken ct) =>
        Ok(await _ops.RecordStockAsync(request, ct));
}
