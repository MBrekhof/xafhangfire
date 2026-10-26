using System.Linq.Expressions;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using xafhangfire.Blazor.Server.Handlers;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Tests.Handlers;

// RPT-001: concurrent runs of the same report must not share an output file.
public class ReportHandlerOutputTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "xafhangfire-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IReportExportService _exportService = Substitute.For<IReportExportService>();

    public ReportHandlerOutputTests()
    {
        _exportService.LoadReport(Arg.Any<Expression<Func<ReportDataV2, bool>>>())
            .Returns(_ => new XtraReport());
        _exportService.ExportReportAsync(Arg.Any<XtraReport>(), Arg.Any<ExportTarget>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public async Task GenerateReport_TwoRunsOfSameReport_WriteDistinctFiles()
    {
        var sut = new GenerateReportHandler(
            _exportService,
            Options.Create(new ReportOutputOptions { OutputDirectory = _outputDir }),
            NullLogger<GenerateReportHandler>.Instance);
        var command = new GenerateReportCommand("Project Status Report");

        await Task.WhenAll(sut.ExecuteAsync(command), sut.ExecuteAsync(command));

        var files = Directory.GetFiles(_outputDir);
        files.Should().HaveCount(2);
        files.Should().OnlyContain(f => Path.GetFileName(f).StartsWith("Project Status Report_") && f.EndsWith(".pdf"));
    }

    [Fact]
    public async Task GenerateReport_WhitespaceOutputPath_UsesDefaultDirectory()
    {
        var sut = new GenerateReportHandler(
            _exportService,
            Options.Create(new ReportOutputOptions { OutputDirectory = _outputDir }),
            NullLogger<GenerateReportHandler>.Instance);

        await sut.ExecuteAsync(new GenerateReportCommand("Project Status Report", OutputPath: "  "));

        Directory.GetFiles(_outputDir).Should().ContainSingle();
    }

    [Fact]
    public async Task SendReportEmail_UsesPerRunTempFile_AndDeletesItAfterSending()
    {
        var emailSender = Substitute.For<IEmailSender>();
        // Two runs send in parallel: collections must be thread-safe.
        var sentPaths = new System.Collections.Concurrent.ConcurrentBag<string>();
        var existedWhenSent = new System.Collections.Concurrent.ConcurrentBag<bool>();
        emailSender.SendAsync(default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs(Task.CompletedTask)
            .AndDoes(ci =>
            {
                var path = ((IEnumerable<string>)ci[4]).Single();
                sentPaths.Add(path);
                existedWhenSent.Add(File.Exists(path));
            });
        var sut = new SendReportEmailHandler(_exportService, emailSender, NullLogger<SendReportEmailHandler>.Instance);
        var command = new SendReportEmailCommand("Project Status Report", "a@example.com;b@example.com");

        await Task.WhenAll(sut.ExecuteAsync(command), sut.ExecuteAsync(command));

        sentPaths.Should().HaveCount(4);
        existedWhenSent.Should().AllBeEquivalentTo(true);
        sentPaths.Distinct().Should().HaveCount(2, "each run has its own file, shared by its recipients");
        sentPaths.Should().OnlyContain(p => Path.GetFileName(p) == "Project Status Report.pdf"
                                            && p.StartsWith(SendReportEmailHandler.TempRoot));
        sentPaths.Should().OnlyContain(p => !File.Exists(p) && !Directory.Exists(Path.GetDirectoryName(p)));
    }
}
