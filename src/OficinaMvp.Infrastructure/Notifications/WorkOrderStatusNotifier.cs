using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OficinaMvp.Application.Ports;

namespace OficinaMvp.Infrastructure.Notifications;

public sealed class WorkOrderStatusNotifier : IWorkOrderStatusNotifier
{
    private readonly NotificationOptions _options;
    private readonly ILogger<WorkOrderStatusNotifier> _logger;

    public WorkOrderStatusNotifier(IOptions<NotificationOptions> options, ILogger<WorkOrderStatusNotifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(WorkOrderStatusNotification notification, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Work order {WorkOrderId} status notification: {Status} - {Note}",
                notification.WorkOrderId,
                notification.Status,
                notification.Note);
            return;
        }

        var recipient = string.IsNullOrWhiteSpace(_options.ToOverride)
            ? _options.From
            : _options.ToOverride;

        if (string.IsNullOrWhiteSpace(_options.From) || string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("SMTP notification skipped because sender or recipient is not configured.");
            return;
        }

        using var message = new MailMessage(
            _options.From,
            recipient,
            $"Atualizacao da OS {notification.WorkOrderId}",
            $"Status: {notification.Status}\nData: {notification.ChangedAtUtc:O}\nObservacao: {notification.Note}");

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
