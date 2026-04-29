namespace OficinaMvp.Api.Infrastructure.Security;

public interface ITokenService
{
    string GenerateToken(string username);
}
