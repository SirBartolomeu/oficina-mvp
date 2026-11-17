using System.Security.Cryptography;
using System.Text;

namespace Oficina.Billing;

public static class MercadoPagoWebhookSignature
{
    public static bool IsValid(string? signature, string? requestId, string? dataId, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(dataId) || string.IsNullOrWhiteSpace(secret))
            return false;
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in signature.Split(','))
        {
            var pair = segment.Trim().Split('=', 2);
            if (pair.Length != 2 || !parts.TryAdd(pair[0], pair[1])) return false;
        }
        if (!parts.TryGetValue("ts", out var timestamp) ||
            !parts.TryGetValue("v1", out var suppliedHex) ||
            !long.TryParse(timestamp, out _) || suppliedHex.Length != 64)
            return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(suppliedHex); }
        catch (FormatException) { return false; }
        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
