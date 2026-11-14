using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// Used when no payment provider is configured. Online payment is refused instead of
/// being faked; fees are still collected at the counter (cash, UPI, cheque…).
/// </summary>
public sealed class DisabledPaymentGateway : IPaymentGateway
{
    public string Name => "Not configured";
    public bool IsEnabled => false;

    public Task<GatewayOrder> CreateOrderAsync(decimal amount, string reference, CancellationToken ct = default) =>
        throw Disabled();

    public Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount,
        CancellationToken ct = default) => throw Disabled();

    public Task<GatewayVerification> RefundAsync(string paymentId, decimal amount, CancellationToken ct = default) =>
        Task.FromResult(new GatewayVerification(false, null,
            "Online refunds are not available because no payment gateway is configured.", null));

    private static DomainException Disabled() => new("online_payments_disabled",
        "Online payment is not enabled for this school yet. Please pay at the school office.", 400);
}
