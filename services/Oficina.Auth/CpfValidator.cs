namespace Oficina.Auth;

// Mesma regra de dígitos verificadores usada em DocumentValidator na OS.
public static class CpfValidator
{
    public static bool TryNormalize(string? value, out string cpf)
    {
        cpf = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        cpf = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (cpf.Length != 11) return false;
        var firstDigit = cpf[0];
        if (cpf.All(digit => digit == firstDigit)) return false;
        return cpf[9] - '0' == Verifier(cpf, 9, 10) &&
               cpf[10] - '0' == Verifier(cpf, 10, 11);
    }

    private static int Verifier(string cpf, int count, int firstWeight)
    {
        var sum = 0;
        for (var index = 0; index < count; index++)
            sum += (cpf[index] - '0') * (firstWeight - index);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
