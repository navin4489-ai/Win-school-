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

/// <summary>
/// Stub gateway used until Razorpay/Cashfree credentials are configured.
/// It never contacts the network and is intentionally marked so in configuration.
/// </summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    private readonly ILogger<StubPaymentGateway> _logger;

    public StubPaymentGateway(ILogger<StubPaymentGateway> logger) => _logger = logger;

    public string Name => "Stub";

    public Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Stub payment verification for order {OrderId}, payment {PaymentId}, amount {Amount}",
            orderId, paymentId, amount);
        return Task.FromResult(new GatewayVerification(true, paymentId, null, "stub"));
    }

    public Task<GatewayVerification> RefundAsync(string paymentId, decimal amount, CancellationToken ct = default)
    {
        _logger.LogWarning("Stub refund for payment {PaymentId}, amount {Amount}", paymentId, amount);
        return Task.FromResult(new GatewayVerification(true, paymentId, null, "stub"));
    }
}
