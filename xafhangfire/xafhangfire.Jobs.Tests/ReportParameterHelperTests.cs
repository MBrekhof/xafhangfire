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
