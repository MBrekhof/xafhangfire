using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace xafhangfire.Jobs.Tests;

public class LogOnlyEmailSenderTests
{
    private readonly LogOnlyEmailSender _sut = new(NullLogger<LogOnlyEmailSender>.Instance);

    [Fact]
    public async Task SendAsync_CompletesWithoutThrowing()
    {
        var act = () => _sut.SendAsync(
            "user@example.com",
            "Subject",
            "<p>Body</p>",
            isHtml: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WithAttachments_CompletesWithoutThrowing()
    {
        var attachments = new List<string> { "/path/file.pdf", "/path/file2.xlsx" };

        var act = () => _sut.SendAsync(
            "user@example.com",
            "Subject",
            "Body",
            isHtml: false,
            attachments);

        await act.Should().NotThrowAsync();
    }
}
