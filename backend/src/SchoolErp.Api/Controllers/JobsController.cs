using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolErp.Infrastructure.Services;

namespace SchoolErp.Api.Controllers;

/// <summary>
/// Lets an external scheduler (a Render Cron Job) run the background jobs. On hosts that
/// sleep when idle, the in-process timers stop, so the cron job wakes the API and runs
/// the work explicitly. Protected by a shared secret, not a user token.
/// </summary>
[ApiController]
[Route("api/jobs")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class JobsController : ControllerBase
{
    private readonly FeePenaltyService _penalties;
    private readonly NotificationDispatcher _notifications;
    private readonly IConfiguration _config;
    private readonly ILogger<JobsController> _logger;

    public JobsController(FeePenaltyService penalties, NotificationDispatcher notifications,
        IConfiguration config, ILogger<JobsController> logger)
    {
        _penalties = penalties;
        _notifications = notifications;
        _config = config;
        _logger = logger;
    }

    /// <summary>Accrues today's late fees (idempotent per day) and sends pending notifications.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var expected = _config["Jobs:Secret"];
        var provided = Request.Headers["X-Job-Secret"].ToString();

        // Disabled unless a secret is configured; compared in constant time.
        if (string.IsNullOrEmpty(expected) || expected.Length < 24 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            return NotFound();

        var started = DateTime.UtcNow;
        await _penalties.RunOnceAsync(ct);
        await _notifications.DispatchBatchAsync(ct);
        _logger.LogInformation("Scheduled jobs ran in {Ms} ms.", (DateTime.UtcNow - started).TotalMilliseconds);

        return Ok(new { ranAtUtc = started, jobs = new[] { "fee-penalties", "notifications" } });
    }
}
