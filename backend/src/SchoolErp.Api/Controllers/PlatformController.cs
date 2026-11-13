using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Platform;

namespace SchoolErp.Api.Controllers;

/// <summary>Super-admin onboarding and configuration of schools. Every action is platform-admin only.</summary>
[ApiController]
[Route("api/platform")]
[Authorize]
public sealed class PlatformController : ControllerBase
{
    private readonly PlatformService _platform;

    public PlatformController(PlatformService platform) => _platform = platform;

    [HttpGet("modules")]
    public ActionResult<IReadOnlyList<ModuleDto>> Modules() => Ok(_platform.GetModules());

    [HttpGet("schools")]
    public async Task<ActionResult<List<SchoolSummaryDto>>> Schools(CancellationToken ct) =>
        Ok(await _platform.GetSchoolsAsync(ct));

    [HttpGet("schools/{id:int}")]
    public async Task<ActionResult<SchoolSummaryDto>> School(int id, CancellationToken ct) =>
        Ok(await _platform.GetSchoolAsync(id, ct));

    [HttpPost("schools")]
    public async Task<ActionResult<SchoolSummaryDto>> CreateSchool(CreateSchoolRequest request, CancellationToken ct) =>
        Ok(await _platform.CreateSchoolAsync(request, ct));

    [HttpPut("schools/{id:int}")]
    public async Task<ActionResult<SchoolSummaryDto>> UpdateSchool(int id, SaveSchoolRequest request,
        CancellationToken ct) => Ok(await _platform.UpdateSchoolAsync(id, request, ct));

    [HttpPost("schools/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, SetActiveRequest request, CancellationToken ct)
    {
        await _platform.SetSchoolActiveAsync(id, request.IsActive, ct);
        return NoContent();
    }
}

/// <summary>
/// User and role administration. A school admin manages their own school; the super admin
/// may pass <c>schoolId</c> to manage any school.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = "permission:users.manage")]
public sealed class UsersController : ControllerBase
{
    private readonly PlatformService _platform;

    public UsersController(PlatformService platform) => _platform = platform;

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List([FromQuery] int? schoolId, [FromQuery] string? search,
        CancellationToken ct) => Ok(await _platform.GetUsersAsync(schoolId, search, ct));

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleOptionDto>>> Roles([FromQuery] int? schoolId, CancellationToken ct) =>
        Ok(await _platform.GetRolesAsync(schoolId, ct));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        Ok(await _platform.CreateUserAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest request, CancellationToken ct) =>
        Ok(await _platform.UpdateUserAsync(id, request, ct));

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        await _platform.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}
