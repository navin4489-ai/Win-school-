using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Operations;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/hr")]
public sealed class HrController : ControllerBase
{
    private readonly OperationsService _ops;

    public HrController(OperationsService ops) => _ops = ops;

    [HttpGet("employees")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<EmployeeDto>>> Employees([FromQuery] string? search,
        CancellationToken ct) => Ok(await _ops.GetEmployeesAsync(search, ct));

    [HttpPost("employees")]
    [Authorize(Policy = "permission:hr.manage")]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee(SaveEmployeeRequest request,
        CancellationToken ct) => Ok(await _ops.CreateEmployeeAsync(request, ct));

    [HttpGet("departments")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<LookupDto>>> Departments(CancellationToken ct) =>
        Ok(await _ops.GetDepartmentsAsync(ct));

    [HttpGet("designations")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<LookupDto>>> Designations(CancellationToken ct) =>
        Ok(await _ops.GetDesignationsAsync(ct));

    [HttpGet("leaves")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<LeaveDto>>> Leaves([FromQuery] LeaveStatus? status,
        CancellationToken ct) => Ok(await _ops.GetLeavesAsync(status, ct));

    [HttpPost("leaves")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<LeaveDto>> ApplyLeave(ApplyLeaveRequest request, CancellationToken ct) =>
        Ok(await _ops.ApplyLeaveAsync(request, ct));

    [HttpPost("leaves/{id:int}/approve")]
    [Authorize(Policy = "permission:hr.manage")]
    public async Task<IActionResult> ApproveLeave(int id, [FromQuery] bool approve,
        [FromQuery] string? remarks, CancellationToken ct)
    {
        await _ops.ApproveLeaveAsync(id, approve, remarks, ct);
        return NoContent();
    }

    [HttpGet("payroll")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<PayrollRunDto>>> Payroll(CancellationToken ct) =>
        Ok(await _ops.GetPayrollRunsAsync(ct));

    [HttpPost("payroll")]
    [Authorize(Policy = "permission:hr.manage")]
    public async Task<ActionResult<PayrollRunDto>> RunPayroll(RunPayrollRequest request, CancellationToken ct) =>
        Ok(await _ops.RunPayrollAsync(request, ct));

    [HttpGet("payroll/{runId:int}/payslips")]
    [Authorize(Policy = "permission:hr.read")]
    public async Task<ActionResult<List<PayslipDto>>> Payslips(int runId, CancellationToken ct) =>
        Ok(await _ops.GetPayslipsAsync(runId, ct));
}
