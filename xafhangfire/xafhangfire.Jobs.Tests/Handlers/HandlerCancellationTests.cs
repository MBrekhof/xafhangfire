using System.Linq.Expressions;
using DevExpress.ExpressApp;
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
using xafhangfire.Jobs.Handlers;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Jobs.Tests.Handlers;

// JOB-014: a cancelled job ends cancelled (so JOB-011 records it as interrupted and Hangfire requeues it),
// never as a success, and leaves no half-written output behind.
public class HandlerCancellationTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "xafhangfire-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cts = new();

    public void Dispose()
    {
        _cts.Dispose();
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public async Task GenerateReport_CancelledDuringExport_ThrowsAndLeavesNoFile()
    {
        // DevExpress's export takes no token: the stop arrives while it runs.
        var sut = ReportHandler(() => { _cts.Cancel(); return new MemoryStream([1, 2, 3]); });

        var act = () => sut.ExecuteAsync(new GenerateReportCommand("Project Status Report"), _cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_outputDir).Should().BeEmpty("a cancelled run must not leave an empty or partial report");
    }

    [Fact]
    public async Task GenerateReport_CancelledMidCopy_DeletesPartialFile()
    {
        var sut = ReportHandler(() => new CancelAfterFirstReadStream(_cts));

        var act = () => sut.ExecuteAsync(new GenerateReportCommand("Project Status Report"), _cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_outputDir).Should().BeEmpty("the partly written report must be removed");
    }

    [Fact]
    public async Task GenerateReport_CancelledDuringExport_LeavesExistingExplicitFileUntouched()
    {
        Directory.CreateDirectory(_outputDir);
        var path = Path.Combine(_outputDir, "fixed.pdf");
        await File.WriteAllTextAsync(path, "previous run");
        var sut = ReportHandler(() => { _cts.Cancel(); return new MemoryStream([1, 2, 3]); });

        var act = () => sut.ExecuteAsync(new GenerateReportCommand("Project Status Report", OutputPath: path), _cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(path)).Should().Be("previous run");
    }

    [Fact]
    public async Task MailMerge_CancellationWrappedBySender_StillThrows()
    {
        // MailKit wraps a cancelled TLS handshake in SslHandshakeException (and swallows one during QUIT).
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs<Task>(_ =>
            {
                _cts.Cancel();
                throw new InvalidOperationException("handshake failed", new OperationCanceledException(_cts.Token));
            });

        var act = () => MailMerge(sender, Contact("Ann")).ExecuteAsync(Command, _cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MailMerge_CancelledDuringLastSend_Throws()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs<Task>(_ => { _cts.Cancel(); throw new OperationCanceledException(_cts.Token); });

        var act = () => MailMerge(sender, Contact("Ann")).ExecuteAsync(Command, _cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a cancelled run must not be recorded as a success");
    }

    [Fact]
    public async Task MailMerge_SendFailure_CountsAsFailedRecipientAndContinues()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync("ann@example.com", default!, default!, default, default, default)
            .ReturnsForAnyArgs<Task>(ci => (string)ci[0] == "ann@example.com"
                ? throw new InvalidOperationException("SMTP said no")
                : Task.CompletedTask);

        await MailMerge(sender, Contact("Ann"), Contact("Bob")).ExecuteAsync(Command, _cts.Token);

        await sender.Received(1).SendAsync("bob@example.com", Arg.Any<string>(), Arg.Any<string>(), true,
            Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MailMerge_TimeoutWhileJobNotCancelled_CountsAsFailedRecipientAndContinues()
    {
        // An SMTP client timeout surfaces as a TaskCanceledException without the job's token being cancelled.
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs<Task>(ci => (string)ci[0] == "ann@example.com"
                ? throw new TaskCanceledException("timeout")
                : Task.CompletedTask);

        await MailMerge(sender, Contact("Ann"), Contact("Bob")).ExecuteAsync(Command, _cts.Token);

        await sender.Received(1).SendAsync("bob@example.com", Arg.Any<string>(), Arg.Any<string>(), true,
            Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>());
    }

    private GenerateReportHandler ReportHandler(Func<Stream> exported)
    {
        var export = Substitute.For<IReportExportService>();
        export.LoadReport(Arg.Any<Expression<Func<ReportDataV2, bool>>>()).Returns(_ => new XtraReport());
        export.ExportReportAsync(Arg.Any<XtraReport>(), Arg.Any<ExportTarget>()).Returns(_ => Task.FromResult(exported()));
        return new GenerateReportHandler(
            export,
            Options.Create(new ReportOutputOptions { OutputDirectory = _outputDir }),
            NullLogger<GenerateReportHandler>.Instance);
    }

    // Hands out some bytes, then the job is stopped while the copy is still running.
    private sealed class CancelAfterFirstReadStream(CancellationTokenSource cts) : MemoryStream(new byte[100])
    {
        private bool _readOnce;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_readOnce)
            {
                _readOnce = true;
                return base.ReadAsync(buffer[..10], cancellationToken);
            }
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }

        // MemoryStream's own CopyToAsync bypasses ReadAsync; use the base Stream loop.
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            CopyViaReadAsync(destination, cancellationToken);

        private async Task CopyViaReadAsync(Stream destination, CancellationToken cancellationToken)
        {
            var buffer = new byte[16];
            int read;
            while ((read = await ReadAsync(buffer, cancellationToken)) > 0)
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static readonly SendMailMergeCommand Command = new("Welcome");

    private static Contact Contact(string name) =>
        new() { FirstName = name, LastName = "Test", Email = name.ToLowerInvariant() + "@example.com" };

    private static SendMailMergeHandler MailMerge(IEmailSender sender, params Contact[] contacts)
    {
        var objectSpace = Substitute.For<IObjectSpace>();
        objectSpace.TypesInfo.FindTypeInfo("xafhangfire.Module.BusinessObjects.EmailTemplate").Type.Returns(typeof(EmailTemplate));
        objectSpace.TypesInfo.FindTypeInfo("xafhangfire.Module.BusinessObjects.Contact").Type.Returns(typeof(Contact));
        objectSpace.GetObjects(typeof(EmailTemplate)).Returns(
            new List<EmailTemplate> { new() { Name = "Welcome", Subject = "Hi {FirstName}", BodyHtml = "<p>Hi</p>" } });
        objectSpace.GetObjects(typeof(Contact)).Returns(contacts.ToList());
        var factory = Substitute.For<INonSecuredObjectSpaceFactory>();
        factory.CreateNonSecuredObjectSpace(typeof(object)).Returns(objectSpace);
        return new SendMailMergeHandler(factory, sender, NullLogger<SendMailMergeHandler>.Instance);
    }
}
