namespace xafhangfire.Jobs;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default);
}
