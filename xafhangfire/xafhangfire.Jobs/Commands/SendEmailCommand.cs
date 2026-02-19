namespace xafhangfire.Jobs.Commands;

public record SendEmailCommand(
    string To,
    string Subject,
    string Body,
    bool IsHtml = true,
    IReadOnlyList<string>? AttachmentPaths = null);
