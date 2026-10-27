#nullable enable
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.Handlers;

public sealed class GenerateReportHandler(
    IReportExportService reportExportService,
    IOptions<ReportOutputOptions> reportOptions,
    ILogger<GenerateReportHandler> logger) : IJobHandler<GenerateReportCommand>
{
    public async Task ExecuteAsync(
        GenerateReportCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating report '{ReportName}' as {Format}",
            command.ReportName, command.OutputFormat);

        using var report = reportExportService.LoadReport<ReportDataV2>(
            r => r.DisplayName == command.ReportName);

        reportExportService.SetupReport(report);
        ReportParameterHelper.ApplyParameters(report, command.ReportParameters, logger);

        // Default file names are unique per run: concurrent runs of the same report must not
        // share (and lock or overwrite) one file (RPT-001). An explicit OutputPath is the caller's call.
        var outputPath = !string.IsNullOrWhiteSpace(command.OutputPath)
            ? command.OutputPath
            : Path.Combine(
                ResolveOutputDirectory(),
                $"{command.ReportName}_{DateTime.Now:yyyyMMdd-HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.{command.OutputFormat.ToLowerInvariant()}");

        var outputDir = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDir);

        var exportTarget = command.OutputFormat.ToLowerInvariant() switch
        {
            "pdf" => ExportTarget.Pdf,
            "xlsx" => ExportTarget.Xlsx,
            _ => throw new ArgumentException($"Unsupported output format: '{command.OutputFormat}'")
        };

        await using var stream = await reportExportService.ExportReportAsync(report, exportTarget);
        // JOB-014: DX's export takes no token; check it before touching (and truncating) the output file.
        cancellationToken.ThrowIfCancellationRequested();
        var fileStream = File.Create(outputPath);
        try
        {
            await using (fileStream)
                await stream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            // No file unless it was written completely. Best effort: a failed delete must not replace the
            // original exception (a cancellation would then be recorded as a failure).
            try { File.Delete(outputPath); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete partial report {Path}", outputPath); }
            throw;
        }

        logger.LogInformation("Report '{ReportName}' exported to {Path}",
            command.ReportName, outputPath);
    }

    private string ResolveOutputDirectory()
    {
        var dir = reportOptions.Value.OutputDirectory;
        return Path.IsPathRooted(dir) ? dir : Path.Combine(Directory.GetCurrentDirectory(), dir);
    }
}
