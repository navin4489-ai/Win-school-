using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;
using SchoolErp.Application.Notifications;
using SchoolErp.Infrastructure.Services;

namespace SchoolErp.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;
    private readonly NotificationHub _hub;
    private readonly ICurrentUser _user;
    private readonly JsonSerializerOptions _jsonOptions;

    public NotificationsController(NotificationService notifications, NotificationHub hub, ICurrentUser user,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _notifications = notifications;
        _hub = hub;
        _user = user;
        // The stream must serialise exactly like every other endpoint, so the
        // browser sees the same camelCase shape it gets from the controllers.
        _jsonOptions = jsonOptions.Value.SerializerOptions;
    }

    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> List([FromQuery] bool unreadOnly,
        CancellationToken ct) => Ok(await _notifications.ListAsync(unreadOnly, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct) =>
        Ok(await _notifications.CountUnreadAsync(ct));

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(ct);
        return NoContent();
    }

    [HttpPost("broadcast")]
    [Authorize(Policy = "permission:communication.manage")]
    public async Task<ActionResult<int>> Broadcast(BroadcastRequest request, CancellationToken ct) =>
        Ok(new { recipients = await _notifications.BroadcastAsync(request, ct) });

    /// <summary>
    /// Server-sent events stream of notifications addressed to the signed-in user.
    ///
    /// The browser subscribes with fetch() rather than EventSource because EventSource
    /// cannot send the bearer token, and putting the token in the query string would leak
    /// it into proxy and access logs.
    /// </summary>
    [HttpGet("stream")]
    [DisableRateLimiting]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache,no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.ContentType = "text/event-stream; charset=utf-8";

        using var subscription = _hub.Subscribe(_user.UserId, _user.GuardianId, out var reader);

        try
        {
            await Response.WriteAsync("retry: 5000\n\n", ct);
            await Response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                // The wait is bounded so an idle connection still emits a comment frame
                // and proxies do not drop it.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var wait = reader.WaitToReadAsync(ct).AsTask();
                var keepAlive = Task.Delay(TimeSpan.FromSeconds(20), timeout.Token);

                var completed = await Task.WhenAny(wait, keepAlive);
                if (completed == keepAlive)
                {
                    await Response.WriteAsync(": keep-alive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }

                timeout.Cancel();
                if (!await wait) break;

                while (reader.TryRead(out var notification))
                {
                    var payload = JsonSerializer.Serialize(new NotificationDto
                    {
                        Id = notification.Id,
                        Title = notification.Title,
                        Message = notification.Message,
                        Category = notification.Category,
                        Channel = notification.Channel,
                        Status = notification.Status,
                        CreatedAtUtc = notification.CreatedAtUtc,
                        ReadAtUtc = notification.ReadAtUtc
                    }, _jsonOptions);

                    await Response.WriteAsync($"event: notification\ndata: {payload}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The browser navigated away or the connection was closed by the client.
        }
    }
}
