using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Operations;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/transport")]
[Authorize(Policy = "permission:transport.manage")]
public sealed class TransportController : ControllerBase
{
    private readonly OperationsService _ops;

    public TransportController(OperationsService ops) => _ops = ops;

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<VehicleDto>>> Vehicles(CancellationToken ct) =>
        Ok(await _ops.GetVehiclesAsync(ct));

    [HttpPost("vehicles")]
    public async Task<ActionResult<VehicleDto>> CreateVehicle(SaveVehicleRequest request, CancellationToken ct) =>
        Ok(await _ops.CreateVehicleAsync(request, ct));

    [HttpGet("routes")]
    public async Task<ActionResult<List<TransportRouteDto>>> Routes(CancellationToken ct) =>
        Ok(await _ops.GetRoutesAsync(ct));

    [HttpPost("routes")]
    public async Task<ActionResult<TransportRouteDto>> CreateRoute(SaveTransportRouteRequest request,
        CancellationToken ct) => Ok(await _ops.CreateRouteAsync(request, ct));

    [HttpGet("allocations")]
    public async Task<ActionResult<List<TransportAllocationDto>>> Allocations(CancellationToken ct) =>
        Ok(await _ops.GetAllocationsAsync(ct));

    [HttpPost("allocations")]
    public async Task<ActionResult<TransportAllocationDto>> Allocate(AllocateTransportRequest request,
        CancellationToken ct) => Ok(await _ops.AllocateTransportAsync(request, ct));
}
