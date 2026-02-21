using FluentAssertions;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Tests;

public class CommandMetadataProviderTests
{
    [Fact]
    public void GetMetadata_DemoLogCommand_ReturnsCorrectParameters()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(DemoLogCommand));

        metadata.Should().NotBeNull();
        metadata.Should().HaveCount(2);

        metadata![0].Name.Should().Be("Message");
        metadata[0].ParameterType.Should().Be(typeof(string));
        metadata[0].IsRequired.Should().BeTrue();
        metadata[0].DefaultValue.Should().BeNull();

        metadata[1].Name.Should().Be("DelaySeconds");
        metadata[1].ParameterType.Should().Be(typeof(int));
        metadata[1].IsRequired.Should().BeFalse();
        metadata[1].DefaultValue.Should().Be(3);
    }

    [Fact]
    public void GetMetadata_ListUsersCommand_ReturnsCorrectParameters()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(ListUsersCommand));

        metadata.Should().NotBeNull();
        metadata.Should().HaveCount(1);
        metadata![0].Name.Should().Be("MaxResults");
        metadata[0].DefaultValue.Should().Be(10);
    }

    [Fact]
    public void GetMetadata_GenerateReportCommand_IncludesComplexTypes()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(GenerateReportCommand));

        metadata.Should().NotBeNull();
        var reportParams = metadata!.FirstOrDefault(m => m.Name == "ReportParameters");
        reportParams.Should().NotBeNull();
        reportParams!.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void GetMetadata_UnknownType_ReturnsNull()
    {
        CommandMetadataProvider.GetMetadata("NonExistentCommand").Should().BeNull();
    }

    [Fact]
    public void GetRegisteredTypeNames_ReturnsAllCommandNames()
    {
        var names = CommandMetadataProvider.GetRegisteredTypeNames();

        names.Should().Contain(nameof(DemoLogCommand));
        names.Should().Contain(nameof(ListUsersCommand));
        names.Should().Contain(nameof(GenerateReportCommand));
        names.Should().Contain(nameof(SendEmailCommand));
        names.Should().Contain(nameof(SendReportEmailCommand));
        names.Should().Contain(nameof(SendMailMergeCommand));
    }

    [Fact]
    public void GetMetadata_SendEmailCommand_HasRequiredFields()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(SendEmailCommand));

        metadata.Should().NotBeNull();
        var toParam = metadata!.First(m => m.Name == "To");
        toParam.IsRequired.Should().BeTrue();
        toParam.ParameterType.Should().Be(typeof(string));
    }
}
