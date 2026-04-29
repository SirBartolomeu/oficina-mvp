namespace OficinaMvp.Api.Application.Contracts;

public sealed record TokenRequest(string Username, string Password);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);
