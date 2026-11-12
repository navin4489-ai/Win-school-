using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolErp.Application.Notifications;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> List([FromQuery] bool unreadOnly,
        CancellationToken ct) => Ok(await _notifications.ListAsync(unreadOnly, ct));

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("broadcast")]
    [Authorize(Policy = "permission:communication.manage")]
    public async Task<ActionResult<int>> Broadcast(BroadcastRequest request, CancellationToken ct) =>
        Ok(new { recipients = await _notifications.BroadcastAsync(request, ct) });
}
