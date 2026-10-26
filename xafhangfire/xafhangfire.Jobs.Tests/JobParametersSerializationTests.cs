using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using xafhangfire.Blazor.Server.Editors;

namespace xafhangfire.Jobs.Tests;

// SER-000..003: the parameter editor's JSON ⇄ field round-trip.
public class JobParametersSerializationTests
{
    private sealed class TestOrganization
    {
        [Key] public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static readonly List<DiscoveredParameterInfo> ProjectStatusParams =
    [
        new() { Name = "StartDate", DisplayName = "Start Date", ClrType = typeof(DateTime), DefaultValue = string.Empty },
        new() { Name = "EndDate", DisplayName = "End Date", ClrType = typeof(DateTime), DefaultValue = string.Empty },
    ];

    private static readonly List<DiscoveredParameterInfo> ContactListParams =
    [
        new() { Name = "Organization", DisplayName = "Organization", ClrType = typeof(TestOrganization), DefaultValue = string.Empty },
    ];

    private static List<ParameterFieldModel> Load(string json, string jobType, List<DiscoveredParameterInfo>? reportParams = null) =>
        JobParametersJsonSerializer.LoadFields(
            json,
            CommandMetadataProvider.GetMetadata(jobType)!,
            _ => reportParams ?? new List<DiscoveredParameterInfo>(),
            _ => new List<LookupItem>(),
            _ => new List<string>());

    private static void ShouldRoundTrip(string json, string jobType, List<DiscoveredParameterInfo>? reportParams = null)
    {
        var reserialized = JobParametersJsonSerializer.SerializeFields(Load(json, jobType, reportParams));

        JsonNode.DeepEquals(JsonNode.Parse(reserialized), JsonNode.Parse(json))
            .Should().BeTrue($"load → serialize without edits must not change the JSON, got {reserialized}");
    }

    [Fact]
    public void RoundTrip_RelativeTermAndIsoDate_Unchanged()
    {
        ShouldRoundTrip(
            """{"ReportName":"Project Status Report","OutputFormat":"Pdf","ReportParameters":{"StartDate":"last-month","EndDate":"2026-01-31"}}""",
            "GenerateReportCommand", ProjectStatusParams);
    }

    [Fact]
    public void RoundTrip_SendEmailWithoutAttachments_Unchanged()
    {
        ShouldRoundTrip("""{"To":"a@example.com","Subject":"Hi","Body":"Hello","IsHtml":false}""", "SendEmailCommand");
    }

    [Fact]
    public void RoundTrip_SendEmailWithAttachments_Unchanged()
    {
        ShouldRoundTrip("""{"To":"a@example.com","Subject":"Hi","Body":"Hello","IsHtml":false,"AttachmentPaths":["a.pdf","b.pdf"]}""", "SendEmailCommand");
    }

    [Fact]
    public void RoundTrip_OrganizationLookup_Unchanged()
    {
        ShouldRoundTrip(
            """{"ReportName":"Contact List by Organization","OutputFormat":"Pdf","ReportParameters":{"Organization":"0f8fad5b-d9cb-469f-a165-70867728950e"}}""",
            "GenerateReportCommand", ContactListParams);
    }

    // SER-003: an unset lookup used to come back as "ReportParameters":{"Organization":""}.
    [Fact]
    public void RoundTrip_UnsetOrganizationLookup_AddsNoReportParameters()
    {
        ShouldRoundTrip(
            """{"ReportName":"Contact List by Organization","OutputFormat":"Pdf"}""",
            "GenerateReportCommand", ContactListParams);
    }

    // Clearing ReportName used to turn the old report's parameters into key/value rows.
    [Fact]
    public void ReportCleared_OldParametersAreNotCarriedOver()
    {
        var json = JobParametersJsonSerializer.WithoutReportParameters(
            """{"OutputFormat":"Pdf","ReportParameters":{"StartDate":"last-month"}}""");

        json.Should().NotContain("ReportParameters");
        Load(json, "GenerateReportCommand")
            .Single(f => f.Name == "ReportParameters").KeyValuePairs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"OutputFormat":"Pdf"}""")]
    public void WithoutReportParameters_NothingToRemove_ReturnsInput(string json) =>
        JobParametersJsonSerializer.WithoutReportParameters(json).Should().Be(json);

    // JSON numbers are invariant; under nl-NL "12.50" used to load as 1250 and save as "12,50".
    [Fact]
    public void RoundTrip_Decimal_IsCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            List<DiscoveredParameterInfo> amountParams =
                [new() { Name = "MinAmount", DisplayName = "Min Amount", ClrType = typeof(decimal), DefaultValue = string.Empty }];
            const string json = """{"ReportName":"R","OutputFormat":"Pdf","ReportParameters":{"MinAmount":"12.50"}}""";

            Load(json, "GenerateReportCommand", amountParams).Single(f => f.Name == "MinAmount").DecimalValue.Should().Be(12.50m);
            ShouldRoundTrip(json, "GenerateReportCommand", amountParams);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Load_RelativeTerm_CaseInsensitive_BecomesCanonicalTerm()
    {
        var field = Load("""{"ReportName":"P","ReportParameters":{"StartDate":" Last-Month "}}""", "GenerateReportCommand", ProjectStatusParams)
            .Single(f => f.Name == "StartDate");

        field.RelativeTerm.Should().Be("last-month");
        field.DateTimeValue.Should().BeNull();
    }

    [Fact]
    public void Load_UnrecognisedDate_IsKeptVerbatim()
    {
        var fields = Load("""{"ReportName":"P","ReportParameters":{"StartDate":"next-decade"}}""", "GenerateReportCommand", ProjectStatusParams);

        fields.Single(f => f.Name == "StartDate").StringValue.Should().Be("next-decade");
        var json = JsonNode.Parse(JobParametersJsonSerializer.SerializeFields(fields))!;
        json["ReportParameters"]!["StartDate"]!.GetValue<string>().Should().Be("next-decade");
    }

    [Fact]
    public void Serialize_EmptyJsonField_IsOmitted()
    {
        var fields = Load("""{"To":"a@example.com","Subject":"s","Body":"b"}""", "SendEmailCommand");
        fields.Single(f => f.Name == "AttachmentPaths").StringValue = "  ";

        var json = JobParametersJsonSerializer.SerializeFields(fields);

        JsonNode.Parse(json)!.AsObject().ContainsKey("AttachmentPaths").Should().BeFalse();
        // the dispatcher can deserialize it (it threw on "AttachmentPaths":"")
        var act = () => JsonSerializer.Deserialize<xafhangfire.Jobs.Commands.SendEmailCommand>(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_EmptyGroupValues_AreOmitted()
    {
        var fields = new List<ParameterFieldModel>
        {
            new() { Name = "Organization", FieldType = "lookup", StringValue = "", GroupName = "ReportParameters" },
            new() { Name = "StartDate", FieldType = "date", GroupName = "ReportParameters" },
        };

        var json = JsonNode.Parse(JobParametersJsonSerializer.SerializeFields(fields))!.AsObject();

        json.ContainsKey("ReportParameters").Should().BeFalse("no group value was set");
    }

    [Fact]
    public void Serialize_DateTerm_WinsOverStaleValues()
    {
        var fields = new List<ParameterFieldModel>
        {
            new() { Name = "StartDate", FieldType = "date", GroupName = "ReportParameters", RelativeTerm = "this-year" },
        };

        var json = JsonNode.Parse(JobParametersJsonSerializer.SerializeFields(fields))!;

        json["ReportParameters"]!["StartDate"]!.GetValue<string>().Should().Be("this-year");
    }

    [Fact]
    public void DateRangeResolver_EveryOfferedTerm_Resolves()
    {
        foreach (var term in DateRangeResolver.Terms)
        {
            var act = () => DateRangeResolver.Resolve(term);
            act.Should().NotThrow($"'{term}' is offered in the editor");
        }
    }
}
