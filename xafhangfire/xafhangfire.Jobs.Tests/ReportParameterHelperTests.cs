using System.Reflection;
using DevExpress.XtraReports.UI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using xafhangfire.Blazor.Server.Handlers;

namespace xafhangfire.Jobs.Tests;

public class ReportParameterHelperTests
{
    [Fact]
    public void ApplyParameters_AutoCreatedDateTerms_CreateDateTimeParameters()
    {
        using var report = new XtraReport();
        var parameters = new Dictionary<string, string>
        {
            ["StartDate"] = "last-month",
            ["EndDate"] = "last-month"
        };

        InvokeApplyParameters(report, parameters);

        var expected = DateRangeResolver.Resolve("last-month");
        var start = report.Parameters["StartDate"];
        var end = report.Parameters["EndDate"];

        start.Should().NotBeNull();
        end.Should().NotBeNull();
        start.Type.Should().Be(typeof(DateTime));
        end.Type.Should().Be(typeof(DateTime));
        ((DateTime)start.Value).Should().Be(expected.Start.ToDateTime(TimeOnly.MinValue));
        ((DateTime)end.Value).Should().Be(expected.End.ToDateTime(TimeOnly.MinValue));
    }

    // SER-003: the reports' FilterStrings must treat a missing/empty parameter as "no filter".
    // Shaped like Contact/Project so the real reports' FilterStrings bind: [Organization.Id], [StartDate].
    public sealed record Org(Guid Id);
    public sealed record Row(Org Organization, DateTime StartDate);

    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private static int CountPrintedRows(string filterString, Dictionary<string, string> parameters)
    {
        using var report = new XtraReport
        {
            DataSource = new List<Row>
            {
                new(new Org(OrgA), new DateTime(2026, 1, 10)),
                new(new Org(OrgB), new DateTime(2026, 2, 10)),
                new(new Org(OrgB), new DateTime(2026, 3, 10)),
            },
            FilterString = filterString,
            RequestParameters = false,
        };
        var detail = new DetailBand();
        report.Bands.Add(detail);
        var count = 0;
        detail.BeforePrint += (_, _) => count++;

        InvokeApplyParameters(report, parameters);
        report.CreateDocument();
        return count;
    }

    // The real reports' filters, so reverting either report fails these tests.
    private static readonly string OrgFilter = new xafhangfire.Module.Reports.ContactListByOrgReport().FilterString;
    private static readonly string DateFilter = new xafhangfire.Module.Reports.ProjectStatusReport().FilterString;

    [Fact]
    public void DateFilter_EmptyValues_PrintsAllRows() =>
        CountPrintedRows(DateFilter, new() { ["StartDate"] = "", ["EndDate"] = " " }).Should().Be(3);

    [Fact]
    public void OrgFilter_NoParameter_PrintsAllRows() =>
        CountPrintedRows(OrgFilter, new()).Should().Be(3);

    [Fact]
    public void OrgFilter_EmptyValue_PrintsAllRows() =>
        CountPrintedRows(OrgFilter, new() { ["Organization"] = "" }).Should().Be(3);

    [Fact]
    public void OrgFilter_Value_Filters() =>
        CountPrintedRows(OrgFilter, new() { ["Organization"] = OrgB.ToString() }).Should().Be(2);

    [Fact]
    public void DateFilter_OnlyStartDate_LeavesEndOpen() =>
        CountPrintedRows(DateFilter, new() { ["StartDate"] = "2026-02-01" }).Should().Be(2);

    [Fact]
    public void DateFilter_NoParameters_PrintsAllRows() =>
        CountPrintedRows(DateFilter, new()).Should().Be(3);

    [Fact]
    public void ApplyParameters_Decimal_IsCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            using var report = new XtraReport();
            report.Parameters.Add(new DevExpress.XtraReports.Parameters.Parameter { Name = "MinAmount", Type = typeof(decimal) });

            InvokeApplyParameters(report, new() { ["MinAmount"] = "12.50" });

            report.Parameters["MinAmount"].Value.Should().Be(12.50m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ApplyParameters_EmptyValueForExistingParameter_ClearsToNull()
    {
        using var report = new XtraReport();
        report.Parameters.Add(new DevExpress.XtraReports.Parameters.Parameter { Name = "Organization", Type = typeof(Guid), Value = OrgA });

        InvokeApplyParameters(report, new() { ["Organization"] = " " });

        report.Parameters["Organization"].Value.Should().BeNull();
    }

    private static void InvokeApplyParameters(XtraReport report, Dictionary<string, string> parameters)
    {
        var helperType = typeof(GenerateReportHandler).Assembly
            .GetType("xafhangfire.Blazor.Server.Handlers.ReportParameterHelper");
        helperType.Should().NotBeNull();

        var method = helperType!.GetMethod(
            "ApplyParameters",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        method!.Invoke(null, new object[] { report, parameters, NullLogger.Instance });
    }
}
