namespace OficinaMvp.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public string Mode { get; init; } = "Log";
    public string? From { get; init; }
    public string? ToOverride { get; init; }
    public SmtpOptions Smtp { get; init; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public bool EnableSsl { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}
