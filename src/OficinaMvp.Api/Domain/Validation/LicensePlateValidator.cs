using System.Text.RegularExpressions;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Validation;

public static class LicensePlateValidator
{
    private static readonly Regex OldPattern = new("^[A-Z]{3}[0-9]{4}$", RegexOptions.Compiled);
    private static readonly Regex MercosulPattern = new("^[A-Z]{3}[0-9][A-Z][0-9]{2}$", RegexOptions.Compiled);

    public static bool IsValid(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            return false;
        }

        return OldPattern.IsMatch(normalized) || MercosulPattern.IsMatch(normalized);
    }

    public static string Normalize(string? value)
    {
        if (!TryNormalize(value, out var normalized) ||
            (!OldPattern.IsMatch(normalized) && !MercosulPattern.IsMatch(normalized)))
        {
            throw new DomainException("Placa de veículo inválida.");
        }

        return normalized;
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        normalized = value
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized.Length is 7;
    }
}
