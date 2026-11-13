using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// A self-contained payment gateway used for demos, staging and go-live smoke tests
/// before a real PSP (Razorpay, PayU, CCAvenue…) is wired in.
///
/// It mimics the shape of a hosted checkout: the server opens an order, the browser shows
/// a fake payment page, and the client posts the payment id back for verification. The
/// amount is encoded in the order id and re-checked on verification, so a tampered amount
/// or a mismatched payment id is rejected exactly as a real gateway would reject it.
///
/// Swap <see cref="IPaymentGateway"/> in DI to move to a real provider.
/// </summary>
public sealed class DummyPaymentGateway : IPaymentGateway
{
    private const string OrderPrefix = "DUMMY-ORD-";
    private const string PaymentPrefix = "DUMMY-PAY-";
    private static readonly TimeSpan OrderLifetime = TimeSpan.FromMinutes(30);

    private readonly ILogger<DummyPaymentGateway> _logger;
    private readonly TimeProvider _timeProvider;

    public DummyPaymentGateway(ILogger<DummyPaymentGateway> logger) : this(logger, TimeProvider.System)
    {
    }

    public DummyPaymentGateway(ILogger<DummyPaymentGateway> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public string Name => "Razorpay (test mode)";

    public Task<GatewayOrder> CreateOrderAsync(decimal amount, string reference, CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "The order amount must be positive.");

        var amountText = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var orderId = $"{OrderPrefix}{amountText}-{Guid.NewGuid():N}";
        var paymentId = $"{PaymentPrefix}{Guid.NewGuid():N}";
        var expires = _timeProvider.GetUtcNow().UtcDateTime.Add(OrderLifetime);

        _logger.LogInformation("Dummy payment order {OrderId} opened for {Amount} ({Reference}).",
            orderId, amount, reference);

        return Task.FromResult(new GatewayOrder(
            orderId,
            paymentId,
            amount,
            "INR",
            $"/checkout/{orderId}",
            expires));
    }

    public Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || !orderId.StartsWith(OrderPrefix, StringComparison.Ordinal))
            return Task.FromResult(Fail("The payment order is not recognised by the gateway."));

        if (string.IsNullOrWhiteSpace(paymentId) || !paymentId.StartsWith(PaymentPrefix, StringComparison.Ordinal))
            return Task.FromResult(Fail("The payment reference is not recognised by the gateway."));

        if (!TryReadAmount(orderId, out var orderAmount))
            return Task.FromResult(Fail("The payment order could not be read."));

        if (Math.Abs(orderAmount - amount) > 0.01m)
            return Task.FromResult(Fail($"The order was raised for {orderAmount:0.00} but {amount:0.00} was presented."));

        var raw = $"{{\"orderId\":\"{orderId}\",\"paymentId\":\"{paymentId}\",\"amount\":{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},\"status\":\"captured\"}}";
        return Task.FromResult(new GatewayVerification(true, paymentId, null, raw));
    }

    public Task<GatewayVerification> RefundAsync(string paymentId, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId) || !paymentId.StartsWith(PaymentPrefix, StringComparison.Ordinal))
            return Task.FromResult(Fail("The payment reference is not recognised by the gateway."));

        if (amount <= 0) return Task.FromResult(Fail("The refund amount must be positive."));

        var raw = $"{{\"paymentId\":\"{paymentId}\",\"refundAmount\":{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},\"status\":\"refunded\"}}";
        return Task.FromResult(new GatewayVerification(true, paymentId, null, raw));
    }

    private static bool TryReadAmount(string orderId, out decimal amount)
    {
        amount = 0m;
        var body = orderId[OrderPrefix.Length..];
        var separator = body.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0) return false;

        return decimal.TryParse(body[..separator], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out amount);
    }

    private static GatewayVerification Fail(string reason) => new(false, null, reason, null);
}
