using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Oficina.Billing;

public sealed record MercadoPagoQrOptions(string AccessToken, string ExternalPosId)
{
    public bool CheckoutPro { get; init; }
    public bool TestMode { get; init; }
    public string? ReturnUrl { get; init; }
}
public sealed record MercadoPagoQrOrder(string Id, string? QrData, string Status)
{
    public string? CheckoutUrl { get; init; }
}
public sealed record MercadoPagoOrderStatus(string Id, string ExternalReference, string Status,
    string Currency, long TotalCents, long PaidCents, string? PaymentStatus)
{
    public string? StatusDetail { get; init; }
}
public sealed record MercadoPagoRefundSubmission(string OrderId, string Status);

public sealed class MercadoPagoQrClient(HttpClient http, MercadoPagoQrOptions options)
{
    public async Task<MercadoPagoQrOrder> CreateAsync(PaymentRequest payment,
        CancellationToken cancellationToken)
    {
        if (options.CheckoutPro) return await CreateCheckoutAsync(payment, cancellationToken);
        if (string.IsNullOrWhiteSpace(options.AccessToken) || string.IsNullOrWhiteSpace(options.ExternalPosId))
            throw new InvalidOperationException("Credenciais e caixa Mercado Pago são obrigatórios.");
        if (payment.Id == Guid.Empty || payment.TotalCents <= 0)
            throw new ArgumentException("Tentativa de pagamento inválida.");

        var amount = (payment.TotalCents / 100m).ToString("F2", CultureInfo.InvariantCulture);
        var reference = $"pay_{payment.Id:N}";
        var body = new
        {
            type = "qr",
            total_amount = amount,
            description = "Ordem de servico",
            external_reference = reference,
            expiration_time = "PT15M",
            config = new { qr = new { external_pos_id = options.ExternalPosId, mode = "dynamic" } },
            transactions = new { payments = new[] { new { amount } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", payment.Id.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Mercado Pago recusou a criação da order.", null, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = result.RootElement;
        var id = root.GetProperty("id").GetString();
        var status = root.GetProperty("status").GetString();
        var returnedReference = root.GetProperty("external_reference").GetString();
        var returnedAmount = root.GetProperty("total_amount").GetString();
        var qrData = root.GetProperty("type_response").GetProperty("qr_data").GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(qrData) ||
            returnedReference != reference || status != "created" ||
            !decimal.TryParse(returnedAmount, NumberStyles.Number, CultureInfo.InvariantCulture,
                out var parsedAmount) || parsedAmount != payment.TotalCents / 100m)
            throw new InvalidOperationException("Resposta da order não corresponde à tentativa de pagamento.");
        return new MercadoPagoQrOrder(id, qrData, status);
    }

    public async Task<MercadoPagoOrderStatus> GetAsync(string orderId, CancellationToken cancellationToken)
    {
        ValidateOrderId(orderId);
        if (options.CheckoutPro) ValidateTestOrderId(orderId);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"v1/orders/{Uri.EscapeDataString(orderId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Falha ao consultar order no Mercado Pago.", null,
                response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = result.RootElement;
        var returnedId = root.GetProperty("id").GetString();
        var reference = root.GetProperty("external_reference").GetString();
        var status = root.GetProperty("status").GetString();
        var currency = root.GetProperty("currency").GetString();
        var total = ParseCents(root.GetProperty("total_amount").GetString());
        var payments = root.TryGetProperty("transactions", out var transactions) &&
            transactions.TryGetProperty("payments", out var entries) ? entries : default;
        if (returnedId != orderId || string.IsNullOrWhiteSpace(reference) ||
            string.IsNullOrWhiteSpace(status) || currency != "BRL")
            throw new InvalidOperationException("Resposta da consulta de order inválida.");
        if (options.CheckoutPro)
        {
            if (root.TryGetProperty("live_mode", out var live) && live.ValueKind != JsonValueKind.False)
                throw new InvalidOperationException("Order de produção bloqueada.");
            if (payments.ValueKind == JsonValueKind.Undefined ||
                payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() == 0)
            {
                if (status is not ("created" or "canceled" or "expired"))
                    throw new InvalidOperationException("Order sem transações em estado inesperado.");
                if (root.TryGetProperty("total_paid_amount", out var totalPaid) && ParseCents(totalPaid.GetString()) != 0)
                    throw new InvalidOperationException("Order sem transações informa valor pago.");
                return new MercadoPagoOrderStatus(returnedId, reference, status, currency,
                    total, 0, status);
            }
        }
        if (payments.ValueKind != JsonValueKind.Array || payments.GetArrayLength() != 1)
            throw new InvalidOperationException("Quantidade de transações inesperada; requer revisão.");
        var payment = payments[0];
        var paymentStatus = payment.GetProperty("status").GetString();
        var paidCents = payment.TryGetProperty("paid_amount", out var paid)
            ? ParseCents(paid.GetString()) : 0;
        return new MercadoPagoOrderStatus(returnedId, reference, status, currency,
            total, paidCents, paymentStatus)
        {
            StatusDetail = root.TryGetProperty("status_detail", out var detail) ? detail.GetString() : null
        };
    }

    public async Task<MercadoPagoRefundSubmission> RequestRefundAsync(string orderId,
        Guid refundRequestId, CancellationToken cancellationToken)
    {
        ValidateOrderId(orderId);
        if (options.CheckoutPro)
        {
            ValidateTestOrderId(orderId);
            await VerifyTestAccountAsync(cancellationToken);
        }
        if (refundRequestId == Guid.Empty)
            throw new ArgumentException("Identificador do pedido de estorno inválido.");
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new InvalidOperationException("Access Token Mercado Pago é obrigatório.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"v1/orders/{Uri.EscapeDataString(orderId)}/refund");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", refundRequestId.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
            throw new HttpRequestException("Mercado Pago não aceitou o pedido de estorno.",
                null, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var returnedId = result.RootElement.GetProperty("id").GetString();
        var status = result.RootElement.GetProperty("status").GetString();
        if (returnedId != orderId || string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("Resposta do estorno não corresponde à order.");
        return new MercadoPagoRefundSubmission(returnedId, status);
    }

    private static void ValidateOrderId(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 64 ||
            !orderId.StartsWith("ORD", StringComparison.Ordinal) ||
            orderId.Any(ch => !char.IsLetterOrDigit(ch)))
            throw new ArgumentException("Identificador da order inválido.");
    }

    private static void ValidateTestOrderId(string orderId)
    {
        if (!orderId.StartsWith("ORDTST", StringComparison.Ordinal))
            throw new InvalidOperationException("Checkout Pro aceita somente orders de teste.");
    }

    private async Task VerifyTestAccountAsync(CancellationToken ct)
    {
        if (!options.TestMode || string.IsNullOrWhiteSpace(options.AccessToken))
            throw new InvalidOperationException("Checkout Pro requer TestMode e credenciais de teste.");
        // O prefixo APP_USR também existe em produção: verificar a identidade antes de qualquer POST.
        using var request = new HttpRequestMessage(HttpMethod.Get, "users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Não foi possível verificar a conta de teste.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var user = document.RootElement;
        if (!user.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array ||
            !tags.EnumerateArray().Any(tag => tag.GetString() == "test_user") ||
            !user.TryGetProperty("site_id", out var site) || site.GetString() != "MLB")
            throw new InvalidOperationException("É obrigatória uma conta de teste brasileira verificada.");
    }

    private async Task<MercadoPagoQrOrder> CreateCheckoutAsync(PaymentRequest payment, CancellationToken ct)
    {
        if (payment.Id == Guid.Empty || payment.TotalCents <= 0)
            throw new ArgumentException("Tentativa de pagamento inválida.");
        await VerifyTestAccountAsync(ct);
        var amount = (payment.TotalCents / 100m).ToString("F2", CultureInfo.InvariantCulture);
        var online = new Dictionary<string, object> { ["retries"] = new { allowed = false } };
        if (!string.IsNullOrWhiteSpace(options.ReturnUrl))
        {
            if (!Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var returnUri) ||
                returnUri.Scheme != "https" || returnUri.IsLoopback || !string.IsNullOrEmpty(returnUri.UserInfo))
                throw new InvalidOperationException("ReturnUrl deve ser HTTPS público.");
            online["success_url"] = options.ReturnUrl;
            online["failure_url"] = options.ReturnUrl;
            online["pending_url"] = options.ReturnUrl;
            online["auto_return"] = "all";
        }
        var reference = $"pay_{payment.Id:N}";
        var body = new
        {
            type = "online", processing_mode = "manual", capture_mode = "automatic_async",
            total_amount = amount, external_reference = reference, expiration_time = "PT15M",
            items = new[] { new { title = "Ordem de serviço", quantity = 1, unit_price = amount } },
            config = new { online }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", payment.Id.ToString("D"));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Mercado Pago recusou a criação do checkout.", null, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = result.RootElement;
        var id = root.GetProperty("id").GetString()!;
        ValidateOrderId(id);
        ValidateTestOrderId(id);
        var checkoutUrl = root.GetProperty("checkout_url").GetString();
        if (root.GetProperty("external_reference").GetString() != reference ||
            root.GetProperty("status").GetString() != "created" ||
            root.GetProperty("currency").GetString() != "BRL" ||
            ParseCents(root.GetProperty("total_amount").GetString()) != payment.TotalCents ||
            root.TryGetProperty("live_mode", out var live) && live.ValueKind != JsonValueKind.False ||
            !IsCheckoutUrl(checkoutUrl, id))
            throw new InvalidOperationException("Checkout retornado não corresponde à tentativa de teste.");
        return new MercadoPagoQrOrder(id, null, "created") { CheckoutUrl = checkoutUrl };
    }

    public static bool IsCheckoutUrl(string? value, string orderId) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "www.mercadopago.com.br" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/checkout/v1/redirect" &&
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
            .TryGetValue("order_id", out var id) && id.Count == 1 && id[0] == orderId;

    private static long ParseCents(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Valor monetário inválido na resposta da order.");
        return checked((long)(amount * 100m));
    }
}
