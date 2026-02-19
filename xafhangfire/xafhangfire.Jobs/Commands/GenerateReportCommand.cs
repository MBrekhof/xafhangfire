namespace xafhangfire.Jobs.Commands;

public record GenerateReportCommand(
    string ReportName,
    string OutputFormat = "Pdf",
    string? OutputPath = null);
