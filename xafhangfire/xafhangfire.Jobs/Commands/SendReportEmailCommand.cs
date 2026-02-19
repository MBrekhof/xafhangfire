namespace xafhangfire.Jobs.Commands;

public record SendReportEmailCommand(
    string ReportName,
    string Recipients,
    string OutputFormat = "Pdf",
    string? Subject = null,
    string? BodyText = null);
