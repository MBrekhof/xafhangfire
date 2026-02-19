using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class SendEmailHandler(
    IEmailSender emailSender,
    ILogger<SendEmailHandler> logger) : IJobHandler<SendEmailCommand>
{
    public async Task ExecuteAsync(
        SendEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sending email to '{To}' — Subject: '{Subject}'",
            command.To, command.Subject);

        await emailSender.SendAsync(
            command.To,
            command.Subject,
            command.Body,
            command.IsHtml,
            command.AttachmentPaths,
            cancellationToken);

        logger.LogInformation("Email sent to '{To}'", command.To);
    }
}
