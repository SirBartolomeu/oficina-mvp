using System.Text.RegularExpressions;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Validation;

public static class DocumentValidator
{
    private static readonly Regex NonDigits = new("[^0-9]", RegexOptions.Compiled);

    public static bool IsValid(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            return false;
        }

        return normalized.Length switch
        {
            11 => IsValidCpf(normalized),
            14 => IsValidCnpj(normalized),
            _ => false
        };
    }

    public static string Normalize(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new DomainException("CPF/CNPJ inválido.");
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

        normalized = NonDigits.Replace(value, string.Empty);
        if (normalized.Length is not (11 or 14))
        {
            return false;
        }

        var firstCharacter = normalized[0];
        if (normalized.All(character => character == firstCharacter))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidCpf(string value)
    {
        var firstDigit = CalculateVerifier(value, new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2 });
        if (value[9] - '0' != firstDigit)
        {
            return false;
        }

        var secondDigit = CalculateVerifier(value, new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 });
        return value[10] - '0' == secondDigit;
    }

    private static bool IsValidCnpj(string value)
    {
        var firstDigit = CalculateVerifier(value, new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        if (value[12] - '0' != firstDigit)
        {
            return false;
        }

        var secondDigit = CalculateVerifier(value, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        return value[13] - '0' == secondDigit;
    }

    private static int CalculateVerifier(string value, IReadOnlyList<int> multipliers)
    {
        var sum = 0;
        for (var index = 0; index < multipliers.Count; index++)
        {
            sum += (value[index] - '0') * multipliers[index];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
