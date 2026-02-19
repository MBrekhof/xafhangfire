#nullable enable
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.Handlers;

public sealed class GenerateReportHandler(
    IReportExportService reportExportService,
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

        var outputPath = command.OutputPath
            ?? Path.Combine(
                Directory.GetCurrentDirectory(),
                "reports",
                $"{command.ReportName}.{command.OutputFormat.ToLowerInvariant()}");

        var outputDir = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDir);

        var exportTarget = command.OutputFormat.ToLowerInvariant() switch
        {
            "pdf" => ExportTarget.Pdf,
            "xlsx" => ExportTarget.Xlsx,
            _ => throw new ArgumentException($"Unsupported output format: '{command.OutputFormat}'")
        };

        await using var stream = await reportExportService.ExportReportAsync(report, exportTarget);
        await using var fileStream = File.Create(outputPath);
        await stream.CopyToAsync(fileStream, cancellationToken);

        logger.LogInformation("Report '{ReportName}' exported to {Path}",
            command.ReportName, outputPath);
    }
}
