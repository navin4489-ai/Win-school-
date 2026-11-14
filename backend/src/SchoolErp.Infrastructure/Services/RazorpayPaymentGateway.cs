using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;

namespace SchoolErp.Infrastructure.Services;

public sealed class RazorpayOptions
{
    public const string SectionName = "Payments:Razorpay";
    public string? KeyId { get; set; }
    public string? KeySecret { get; set; }

    public bool HasKeys => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}

/// <summary>
/// Razorpay Standard Checkout. Orders are created server side, the browser collects the
/// payment, and a payment is only credited after the signature is verified and the
/// payment is re-read from Razorpay (status, order, amount and currency must all match).
/// </summary>
public sealed class RazorpayPaymentGateway : IPaymentGateway
{
    public const string BaseUrl = "https://api.razorpay.com/v1/";
    private const string Currency = "INR";
    private const int MaxRawLength = 4000;

    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpayPaymentGateway> _logger;

    public RazorpayPaymentGateway(HttpClient http, IOptions<RazorpayOptions> options,
        ILogger<RazorpayPaymentGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress ??= new Uri(BaseUrl);
        if (_options.HasKeys)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public string Name => "Razorpay";
    public bool IsEnabled => _options.HasKeys;
    public string PublicKey => _options.KeyId ?? string.Empty;

    public static long ToPaise(decimal amount) =>
        (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Razorpay checkout signature: lowercase hex HMAC-SHA256 of "orderId|paymentId".</summary>
    public static string ComputeSignature(string orderId, string paymentId, string keySecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{orderId}|{paymentId}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsSignatureValid(string orderId, string paymentId, string? signature, string keySecret)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var expected = Encoding.UTF8.GetBytes(ComputeSignature(orderId, paymentId, keySecret));
        var actual = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public async Task<GatewayOrder> CreateOrderAsync(decimal amount, string reference, GatewayOrderBinding binding,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var receipt = reference.Length > 40 ? reference[..40] : reference;
        var body = new
        {
            amount = ToPaise(amount),
            currency = Currency,
            receipt,
            notes = new Dictionary<string, string>
            {
                ["reference"] = reference,
                ["schoolId"] = binding.SchoolId.ToString(CultureInfo.InvariantCulture),
                ["studentId"] = binding.StudentId.ToString(CultureInfo.InvariantCulture),
                ["installmentId"] = binding.InstallmentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }
        };

        var (ok, json, raw) = await SendAsync(HttpMethod.Post, "orders", body, ct);
        if (!ok || json is null || !json.Value.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } orderId)
        {
            _logger.LogWarning("Razorpay order creation failed: {Response}", Truncate(raw));
            throw new DomainException("payment_gateway_error",
                $"Could not start the online payment: {ErrorDescription(json) ?? "payment gateway unavailable"}. Please try again.", 502);
        }

        return new GatewayOrder(orderId, string.Empty, amount, Currency, string.Empty, DateTime.UtcNow.AddMinutes(30));
    }

    public async Task<bool> OrderMatchesAsync(string orderId, GatewayOrderBinding binding, decimal amount,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var (ok, json, raw) = await SendAsync(HttpMethod.Get, $"orders/{Uri.EscapeDataString(orderId)}", null, ct);
        if (!ok || json is null)
        {
            _logger.LogWarning("Razorpay order lookup failed for {OrderId}: {Response}", orderId, Truncate(raw));
            return false;
        }

        var order = json.Value;
        if (GetLong(order, "amount") != ToPaise(amount) || GetString(order, "currency") != Currency) return false;
        if (!order.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Object) return false;

        return GetString(notes, "schoolId") == binding.SchoolId.ToString(CultureInfo.InvariantCulture)
               && GetString(notes, "studentId") == binding.StudentId.ToString(CultureInfo.InvariantCulture)
               && (GetString(notes, "installmentId") ?? string.Empty) ==
                  (binding.InstallmentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public async Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, string? signature,
        decimal amount, CancellationToken ct = default)
    {
        EnsureEnabled();
        if (!IsSignatureValid(orderId, paymentId, signature, _options.KeySecret!))
            return new GatewayVerification(false, null, "The payment signature is invalid.", null);

        var (ok, json, raw) = await SendAsync(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}", null, ct);
        if (!ok || json is null)
            return new GatewayVerification(false, null,
                $"The payment could not be confirmed with Razorpay: {ErrorDescription(json) ?? "gateway unavailable"}.", Truncate(raw));

        var payment = json.Value;
        var status = GetString(payment, "status");
        var expectedPaise = ToPaise(amount);

        if (GetString(payment, "order_id") != orderId)
            return new GatewayVerification(false, null, "The payment does not belong to this order.", Truncate(raw));
        if (GetLong(payment, "amount") != expectedPaise || GetString(payment, "currency") != Currency)
            return new GatewayVerification(false, null, "The paid amount does not match the fee amount.", Truncate(raw));
        if (status is not ("captured" or "authorized"))
            return new GatewayVerification(false, null, $"The payment is not complete (status: {status ?? "unknown"}).", Truncate(raw));

        if (status == "authorized")
        {
            (ok, json, raw) = await SendAsync(HttpMethod.Post, $"payments/{Uri.EscapeDataString(paymentId)}/capture",
                new { amount = expectedPaise, currency = Currency }, ct);
            if (!ok || json is null || GetString(json.Value, "status") != "captured")
                return new GatewayVerification(false, null,
                    $"The payment could not be captured: {ErrorDescription(json) ?? "gateway unavailable"}.", Truncate(raw));
        }

        return new GatewayVerification(true, paymentId, null, Truncate(raw));
    }

    public async Task<GatewayVerification> RefundAsync(string paymentId, decimal amount, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return new GatewayVerification(false, null, "Razorpay is not configured.", null);

        var (ok, json, raw) = await SendAsync(HttpMethod.Post, $"payments/{Uri.EscapeDataString(paymentId)}/refund",
            new { amount = ToPaise(amount) }, ct);
        if (!ok || json is null || GetString(json.Value, "id") is not { Length: > 0 } refundId)
            return new GatewayVerification(false, null,
                $"Razorpay refund failed: {ErrorDescription(json) ?? "gateway unavailable"}.", Truncate(raw));

        return new GatewayVerification(true, refundId, null, Truncate(raw));
    }

    private async Task<(bool Ok, JsonElement? Json, string? Raw)> SendAsync(HttpMethod method, string path,
        object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            JsonElement? json = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { json = JsonDocument.Parse(raw).RootElement.Clone(); }
                catch (JsonException) { json = null; }
            }
            return (response.IsSuccessStatusCode, json, raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Razorpay request {Method} {Path} failed", method, path);
            return (false, null, null);
        }
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new DomainException("online_payments_disabled",
                "Online payment is not enabled for this school yet. Please pay at the school office.", 400);
    }

    private static string? ErrorDescription(JsonElement? json) =>
        json is { } j && j.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? GetString(error, "description")
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        } : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) ? number : null;

    private static string? Truncate(string? raw) =>
        raw is null ? null : raw.Length <= MaxRawLength ? raw : raw[..MaxRawLength];
}
