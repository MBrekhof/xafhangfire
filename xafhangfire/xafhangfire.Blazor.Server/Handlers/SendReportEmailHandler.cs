#nullable enable
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraPrinting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.Handlers;

public sealed class SendReportEmailHandler(
    IReportExportService reportExportService,
    IEmailSender emailSender,
    IOptions<ReportOutputOptions> reportOptions,
    ILogger<SendReportEmailHandler> logger) : IJobHandler<SendReportEmailCommand>
{
    public async Task ExecuteAsync(
        SendReportEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating report '{ReportName}' for email delivery to '{Recipients}'",
            command.ReportName, command.Recipients);

        // Export report to configured output directory
        using var report = reportExportService.LoadReport<ReportDataV2>(
            r => r.DisplayName == command.ReportName);
        reportExportService.SetupReport(report);

        var extension = command.OutputFormat.ToLowerInvariant();
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        var tempPath = Path.Combine(outputDir, $"{command.ReportName}.{extension}");

        var exportTarget = extension switch
        {
            "pdf" => ExportTarget.Pdf,
            "xlsx" => ExportTarget.Xlsx,
            _ => throw new ArgumentException($"Unsupported output format: '{command.OutputFormat}'")
        };

        await using var stream = await reportExportService.ExportReportAsync(report, exportTarget);
        await using (var fileStream = File.Create(tempPath))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        // Send email with attachment
        var subject = command.Subject ?? $"Report: {command.ReportName}";
        var body = command.BodyText ?? $"<p>Please find the attached report: <strong>{command.ReportName}</strong></p>";
        var attachments = new List<string> { tempPath };

        foreach (var recipient in command.Recipients.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await emailSender.SendAsync(
                recipient,
                subject,
                body,
                isHtml: true,
                attachments,
                cancellationToken);
        }

        logger.LogInformation("Report '{ReportName}' emailed to '{Recipients}'",
            command.ReportName, command.Recipients);
    }

    private string ResolveOutputDirectory()
    {
        var dir = reportOptions.Value.OutputDirectory;
        return Path.IsPathRooted(dir) ? dir : Path.Combine(Directory.GetCurrentDirectory(), dir);
    }
}
