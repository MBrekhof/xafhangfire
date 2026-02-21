#nullable enable

using System.Reflection;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs;

public static class CommandMetadataProvider
{
    private static readonly Dictionary<string, Type> CommandTypes = new()
    {
        [nameof(DemoLogCommand)] = typeof(DemoLogCommand),
        [nameof(ListUsersCommand)] = typeof(ListUsersCommand),
        [nameof(GenerateReportCommand)] = typeof(GenerateReportCommand),
        [nameof(SendEmailCommand)] = typeof(SendEmailCommand),
        [nameof(SendReportEmailCommand)] = typeof(SendReportEmailCommand),
        [nameof(SendMailMergeCommand)] = typeof(SendMailMergeCommand),
    };

    private static readonly Dictionary<(string Command, string Param), string> DataSourceHints = new()
    {
        [("GenerateReportCommand", "ReportName")] = "Reports",
        [("GenerateReportCommand", "OutputFormat")] = "Pdf,Xlsx",
        [("GenerateReportCommand", "ReportParameters")] = "KeyValue",
        [("SendReportEmailCommand", "ReportName")] = "Reports",
        [("SendReportEmailCommand", "OutputFormat")] = "Pdf,Xlsx",
        [("SendReportEmailCommand", "ReportParameters")] = "KeyValue",
        [("SendMailMergeCommand", "TemplateName")] = "EmailTemplates",
    };

    public static IReadOnlyList<CommandParameterMetadata>? GetMetadata(string jobTypeName)
    {
        if (!CommandTypes.TryGetValue(jobTypeName, out var commandType))
            return null;

        var ctor = commandType.GetConstructors().FirstOrDefault();
        if (ctor == null) return null;

        return ctor.GetParameters().Select(p =>
        {
            DataSourceHints.TryGetValue((jobTypeName, p.Name!), out var hint);

            return new CommandParameterMetadata(
                Name: p.Name!,
                ParameterType: p.ParameterType,
                IsRequired: !p.HasDefaultValue,
                DefaultValue: p.HasDefaultValue ? p.DefaultValue : null,
                DataSourceHint: hint
            );
        }).ToList();
    }

    public static IReadOnlyList<string> GetRegisteredTypeNames() =>
        CommandTypes.Keys.ToList();
}
