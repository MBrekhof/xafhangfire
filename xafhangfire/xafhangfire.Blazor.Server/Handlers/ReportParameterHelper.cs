#nullable enable
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Handlers;

internal static class ReportParameterHelper
{
    public static void ApplyParameters(
        XtraReport report,
        Dictionary<string, string>? parameters,
        ILogger logger)
    {
        if (parameters is null || parameters.Count == 0)
            return;

        foreach (var (key, value) in parameters)
        {
            var param = report.Parameters[key];
            if (param is null)
            {
                logger.LogWarning("Report parameter '{ParameterName}' not found — skipping", key);
                continue;
            }

            // Resolve friendly date terms (e.g., "last-month") via DateRangeResolver
            if (param.Type == typeof(DateTime) || param.Type == typeof(DateOnly))
            {
                if (TryResolveDateParameter(value, key, out var resolved))
                {
                    param.Value = param.Type == typeof(DateOnly)
                        ? resolved
                        : resolved.ToDateTime(TimeOnly.MinValue);
                    logger.LogDebug("Set report parameter '{Name}' = {Value} (resolved from '{Term}')",
                        key, param.Value, value);
                    continue;
                }
            }

            // Direct value assignment — the XtraReport parameter handles type conversion
            param.Value = ConvertValue(value, param.Type);
            logger.LogDebug("Set report parameter '{Name}' = {Value}", key, param.Value);
        }
    }

    private static bool TryResolveDateParameter(string value, string key, out DateOnly resolved)
    {
        resolved = default;

        try
        {
            var range = DateRangeResolver.Resolve(value);
            // Use Start for parameters ending with "Start"/"From", End for "End"/"To"
            resolved = key.EndsWith("End", StringComparison.OrdinalIgnoreCase)
                     || key.EndsWith("To", StringComparison.OrdinalIgnoreCase)
                ? range.End
                : range.Start;
            return true;
        }
        catch (ArgumentException)
        {
            // Not a DateRangeResolver term — fall through to direct parse
        }

        if (DateOnly.TryParse(value, out var parsed))
        {
            resolved = parsed;
            return true;
        }

        return false;
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(int) && int.TryParse(value, out var intVal))
            return intVal;

        if (targetType == typeof(decimal) && decimal.TryParse(value, out var decVal))
            return decVal;

        if (targetType == typeof(bool) && bool.TryParse(value, out var boolVal))
            return boolVal;

        if (targetType == typeof(DateTime) && DateTime.TryParse(value, out var dtVal))
            return dtVal;

        if (targetType == typeof(DateOnly) && DateOnly.TryParse(value, out var doVal))
            return doVal;

        if (targetType == typeof(Guid) && Guid.TryParse(value, out var guidVal))
            return guidVal;

        // Fallback — return the string and let XtraReport attempt conversion
        return value;
    }
}
