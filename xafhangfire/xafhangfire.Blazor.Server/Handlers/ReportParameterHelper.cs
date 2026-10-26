#nullable enable
using System.Globalization;
using DevExpress.XtraReports.Parameters;
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
            // Try lookup by Name first, then fall back to Description
            // (XAF report designer may populate Description instead of Name)
            var param = report.Parameters[key]
                ?? report.Parameters.Cast<Parameter>()
                    .FirstOrDefault(p => string.Equals(p.Description, key, StringComparison.OrdinalIgnoreCase));

            // Empty = not set. An undefined ?param in FilterString evaluates to null, which the
            // reports' "?p Is Null Or …" filters treat as "no filter". Never create a parameter
            // for it: without AllowNull, DevExpress coerces null to ""/Guid.Empty and filters
            // every row out (SER-003).
            if (string.IsNullOrWhiteSpace(value))
            {
                if (param is not null)
                {
                    param.AllowNull = true;
                    param.Value = null;
                    logger.LogDebug("Report parameter '{Name}' cleared (empty value)", key);
                }
                continue;
            }

            // Auto-create the parameter if it doesn't exist on the report.
            // This supports the ReportParametersObjectBase-only pattern where
            // parameters are defined on the parameters class, not on the XtraReport.
            // The FilterString ?-references still need a Parameter object at runtime.
            if (param is null)
            {
                if (TryResolveDateParameter(value, key, out var resolvedDate))
                {
                    param = new Parameter
                    {
                        Name = key,
                        Type = typeof(DateTime),
                        Visible = false,
                        Value = resolvedDate.ToDateTime(TimeOnly.MinValue)
                    };
                    report.Parameters.Add(param);
                    logger.LogDebug("Auto-created report parameter '{Name}' as DateTime = {Value}",
                        key, param.Value);
                    continue;
                }

                var inferredType = InferType(value);
                param = new Parameter
                {
                    Name = key,
                    Type = inferredType,
                    Visible = false
                };
                report.Parameters.Add(param);
                logger.LogDebug("Auto-created report parameter '{Name}' (type: {Type})", key, inferredType.Name);
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

    /// <summary>
    /// Infers the CLR type from a string value so auto-created parameters
    /// get the correct type for FilterString evaluation.
    /// </summary>
    private static Type InferType(string value)
    {
        if (DateTime.TryParse(value, out _))
            return typeof(DateTime);
        if (int.TryParse(value, out _))
            return typeof(int);
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return typeof(decimal);
        if (bool.TryParse(value, out _))
            return typeof(bool);
        if (Guid.TryParse(value, out _))
            return typeof(Guid);
        return typeof(string);
    }

    private static bool TryResolveDateParameter(string value, string key, out DateOnly resolved)
    {
        resolved = default;

        try
        {
            var range = DateRangeResolver.Resolve(value);
            // Use Start for parameters ending with "Start"/"From", End for "End"/"EndDate"/"To"
            resolved = key.EndsWith("End", StringComparison.OrdinalIgnoreCase)
                     || key.EndsWith("EndDate", StringComparison.OrdinalIgnoreCase)
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

        // Stored JSON values are culture-invariant ("12.50"), whatever the server culture is.
        if (targetType == typeof(decimal) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decVal))
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
