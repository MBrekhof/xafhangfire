using Microsoft.Extensions.Logging;

namespace xafhangfire.Jobs;

public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[LogOnlyEmailSender] To: {To} | Subject: {Subject} | IsHtml: {IsHtml} | Attachments: {AttachmentCount}",
            to, subject, isHtml, attachmentPaths?.Count ?? 0);
        logger.LogInformation("[LogOnlyEmailSender] Body:\n{Body}", body);

        return Task.CompletedTask;
    }
}
