using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using xafhangfire.Jobs.Commands;
using xafhangfire.Jobs.Handlers;

namespace xafhangfire.Jobs.Tests.Handlers;

public class SendEmailHandlerTests
{
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly SendEmailHandler _sut;

    public SendEmailHandlerTests()
    {
        _sut = new SendEmailHandler(_emailSender, NullLogger<SendEmailHandler>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToEmailSender()
    {
        var command = new SendEmailCommand(
            To: "user@example.com",
            Subject: "Test Subject",
            Body: "<p>Hello</p>",
            IsHtml: true);

        await _sut.ExecuteAsync(command);

        await _emailSender.Received(1).SendAsync(
            "user@example.com",
            "Test Subject",
            "<p>Hello</p>",
            true,
            Arg.Any<IReadOnlyList<string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesAttachments()
    {
        var attachments = new List<string> { "/path/to/file.pdf" };
        var command = new SendEmailCommand(
            To: "user@example.com",
            Subject: "With Attachment",
            Body: "See attached",
            AttachmentPaths: attachments);

        await _sut.ExecuteAsync(command);

        await _emailSender.Received(1).SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Is<IReadOnlyList<string>?>(a => a != null && a.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var command = new SendEmailCommand("to@test.com", "Sub", "Body");

        await _sut.ExecuteAsync(command, cts.Token);

        await _emailSender.Received(1).SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<string>?>(),
            cts.Token);
    }
}
