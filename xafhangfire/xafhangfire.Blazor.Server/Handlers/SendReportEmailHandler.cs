#nullable enable
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraPrinting;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.Handlers;

public sealed class SendReportEmailHandler(
    IReportExportService reportExportService,
    IEmailSender emailSender,
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
        ReportParameterHelper.ApplyParameters(report, command.ReportParameters, logger);

        var extension = command.OutputFormat.ToLowerInvariant();
        var exportTarget = extension switch
        {
            "pdf" => ExportTarget.Pdf,
            "xlsx" => ExportTarget.Xlsx,
            _ => throw new ArgumentException($"Unsupported output format: '{command.OutputFormat}'")
        };

        // One folder per run: concurrent runs of the same report never share a file (RPT-001),
        // and the attachment keeps its readable "{ReportName}.{ext}" name.
        var runDir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDir);
        try
        {
            var tempPath = Path.Combine(runDir, $"{command.ReportName}.{extension}");

            await using (var stream = await reportExportService.ExportReportAsync(report, exportTarget))
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
        }
        finally
        {
            try
            {
                Directory.Delete(runDir, recursive: true);
            }
            catch (Exception ex)
            {
                // Best effort: a leftover temp folder must not mask the email outcome.
                logger.LogWarning(ex, "Could not delete temporary report folder {Dir}", runDir);
            }
        }

        logger.LogInformation("Report '{ReportName}' emailed to '{Recipients}'",
            command.ReportName, command.Recipients);
    }

    internal static string TempRoot => Path.Combine(Path.GetTempPath(), "xafhangfire-report-email");
}
