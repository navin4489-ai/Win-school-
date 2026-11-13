using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// Development sender: writes the outbound message to the log instead of calling a provider.
/// Replace with WhatsApp Business, SMS and SMTP adapters before go-live (see docs/deployment.md).
/// </summary>
public sealed class LogNotificationSender : INotificationSender
{
    private readonly ILogger<LogNotificationSender> _logger;

    public LogNotificationSender(ILogger<LogNotificationSender> logger) => _logger = logger;

    public Task SendAsync(Notification notification, string? recipient, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Outbound {Channel} notification '{Title}' to {Recipient}: {Message}",
            notification.Channel, notification.Title, recipient ?? "(no recipient)", notification.Message);
        return Task.CompletedTask;
    }
}
