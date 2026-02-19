#nullable enable
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Services;

public sealed class SmtpEmailSender(
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        var smtpHost = configuration["Email:Smtp:Host"]!;
        var smtpPort = configuration.GetValue("Email:Smtp:Port", 587);
        var username = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];
        var useSsl = configuration.GetValue("Email:Smtp:UseSsl", true);
        var fromAddress = configuration["Email:Smtp:FromAddress"] ?? "noreply@example.com";
        var fromName = configuration["Email:Smtp:FromName"] ?? "XAF Hangfire App";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder();
        if (isHtml)
            builder.HtmlBody = body;
        else
            builder.TextBody = body;

        if (attachmentPaths is { Count: > 0 })
        {
            foreach (var path in attachmentPaths)
            {
                if (File.Exists(path))
                    await builder.Attachments.AddAsync(path, cancellationToken);
                else
                    logger.LogWarning("Attachment not found: {Path}", path);
            }
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();

        var secureSocketOptions = useSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(username))
            await client.AuthenticateAsync(username, password, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Email sent via SMTP to '{To}'", to);
    }
}
