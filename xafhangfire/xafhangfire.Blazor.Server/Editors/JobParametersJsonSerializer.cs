using System.Text.Json;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Editors;

/// <summary>
/// JSON ⇄ form fields for the job parameter editor. Pure functions (database lookups come in as
/// delegates) so load → serialize round-trips are unit-testable.
/// Contract: loading a JSON and serializing it again without edits must not change it (SER-000).
/// </summary>
internal static class JobParametersJsonSerializer
{
    public static List<ParameterFieldModel> LoadFields(
        string json,
        IReadOnlyList<CommandParameterMetadata> metadata,
        Func<string, List<DiscoveredParameterInfo>> discoverReportParameters,
        Func<Type, List<LookupItem>> loadLookupItems,
        Func<string, List<string>> resolveDropdownItems)
    {
        var values = ParseJson(json);
        var fields = new List<ParameterFieldModel>();

        foreach (var param in metadata)
        {
            // ReportParameters: one typed field per discovered report parameter
            if (param.DataSourceHint == "ReportParameters")
            {
                var reportName = ExtractReportNameFromValues(values);
                var discovered = string.IsNullOrEmpty(reportName)
                    ? new List<DiscoveredParameterInfo>()
                    : discoverReportParameters(reportName);
                if (discovered.Count > 0)
                {
                    var existingValues = new Dictionary<string, string>();
                    if (values.TryGetValue(param.Name, out var existingElement)
                        && existingElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in existingElement.EnumerateObject())
                        {
                            existingValues[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() ?? string.Empty
                                : prop.Value.GetRawText();
                        }
                    }

                    foreach (var d in discovered)
                        fields.Add(CreateReportParameterField(d, param.Name, existingValues, loadLookupItems));
                    continue;
                }
            }

            fields.Add(CreateCommandField(param, values, resolveDropdownItems));
        }

        return fields;
    }

    private static ParameterFieldModel CreateReportParameterField(
        DiscoveredParameterInfo d,
        string groupName,
        Dictionary<string, string> existingValues,
        Func<Type, List<LookupItem>> loadLookupItems)
    {
        var field = new ParameterFieldModel
        {
            Name = d.Name,
            DisplayName = d.DisplayName,
            GroupName = groupName,
        };

        if (d.ClrType == typeof(DateTime) || d.ClrType == typeof(DateOnly)
            || d.ClrType == typeof(DateTime?) || d.ClrType == typeof(DateOnly?))
            field.FieldType = "date";
        else if (d.ClrType == typeof(int))
            field.FieldType = "int";
        else if (d.ClrType == typeof(decimal) || d.ClrType == typeof(double) || d.ClrType == typeof(float))
            field.FieldType = "decimal";
        else if (d.ClrType == typeof(bool))
            field.FieldType = "bool";
        else if (IsEntityType(d.ClrType))
        {
            field.FieldType = "lookup";
            field.LookupItems = loadLookupItems(d.ClrType);
        }
        else
            field.FieldType = "string";

        var valueStr = existingValues.TryGetValue(d.Name, out var v) ? v : d.DefaultValue;
        switch (field.FieldType)
        {
            case "date":
                SetDateValue(field, valueStr);
                break;
            case "int":
                field.IntValue = int.TryParse(valueStr, out var iv) ? iv : 0;
                break;
            case "decimal":
                field.DecimalValue = decimal.TryParse(valueStr, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var dec) ? dec : 0;
                break;
            case "bool":
                field.BoolValue = bool.TryParse(valueStr, out var b) && b;
                break;
            default: // lookup, string
                field.StringValue = valueStr ?? string.Empty;
                break;
        }

        return field;
    }

    // Order matters: a relative term ("last-month") is not a date, and an unrecognised value is
    // kept verbatim rather than silently dropped (SER-002).
    private static void SetDateValue(ParameterFieldModel field, string valueStr)
    {
        var raw = valueStr?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            return;

        var term = DateRangeResolver.Terms.FirstOrDefault(t => string.Equals(t, raw, StringComparison.OrdinalIgnoreCase));
        if (term != null)
            field.RelativeTerm = term;
        else if (DateTime.TryParse(raw, out var dt))
            field.DateTimeValue = dt;
        else
            field.StringValue = raw;
    }

    private static ParameterFieldModel CreateCommandField(
        CommandParameterMetadata param,
        Dictionary<string, JsonElement> values,
        Func<string, List<string>> resolveDropdownItems)
    {
        var field = new ParameterFieldModel
        {
            Name = param.Name,
            DisplayName = FormatDisplayName(param.Name),
            IsRequired = param.IsRequired,
        };

        // Determine field type based on DataSourceHint first, then CLR type
        if (param.DataSourceHint == "KeyValue" || param.DataSourceHint == "ReportParameters")
            field.FieldType = "keyvalue";
        else if (param.DataSourceHint != null && param.ParameterType == typeof(string))
        {
            field.FieldType = "dropdown";
            field.DropdownItems = resolveDropdownItems(param.DataSourceHint);
        }
        else if (param.ParameterType == typeof(int))
            field.FieldType = "int";
        else if (param.ParameterType == typeof(bool))
            field.FieldType = "bool";
        else if (param.ParameterType == typeof(string))
            field.FieldType = "string";
        else if (param.ParameterType.IsGenericType &&
                 param.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            field.FieldType = "keyvalue";
        else
            field.FieldType = "json";

        if (values.TryGetValue(param.Name, out var element))
        {
            switch (field.FieldType)
            {
                case "int":
                    field.IntValue = element.ValueKind == JsonValueKind.Number
                        ? element.GetInt32()
                        : param.DefaultValue is int def ? def : 0;
                    break;
                case "bool":
                    field.BoolValue = element.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && element.GetBoolean();
                    break;
                case "keyvalue":
                    field.KeyValuePairs = ParseKeyValuePairs(element);
                    break;
                case "json":
                    field.StringValue = element.ValueKind == JsonValueKind.Null ? string.Empty : element.GetRawText();
                    break;
                default: // string, dropdown
                    field.StringValue = element.ValueKind == JsonValueKind.String
                        ? element.GetString() ?? string.Empty
                        : element.GetRawText();
                    break;
            }
        }
        else if (param.DefaultValue != null)
        {
            switch (field.FieldType)
            {
                case "int":
                    field.IntValue = (int)param.DefaultValue;
                    break;
                case "bool":
                    field.BoolValue = (bool)param.DefaultValue;
                    break;
                default:
                    field.StringValue = param.DefaultValue.ToString() ?? string.Empty;
                    break;
            }
        }

        return field;
    }

    public static string SerializeFields(List<ParameterFieldModel> fields)
    {
        var dict = new Dictionary<string, object>();
        var groups = new Dictionary<string, Dictionary<string, string>>();

        foreach (var field in fields)
        {
            if (field.GroupName != null)
            {
                if (!groups.TryGetValue(field.GroupName, out var groupDict))
                {
                    groupDict = new Dictionary<string, string>();
                    groups[field.GroupName] = groupDict;
                }

                var value = field.FieldType switch
                {
                    "date" => field.RelativeTerm
                              ?? field.DateTimeValue?.ToString("yyyy-MM-dd")
                              ?? field.StringValue,
                    "int" => field.IntValue.ToString(),
                    "decimal" => field.DecimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "bool" => field.BoolValue.ToString().ToLowerInvariant(),
                    _ => field.StringValue,
                };
                // An empty value means "not set": omit it so the report sees no parameter
                // instead of "" (SER-003).
                if (!string.IsNullOrEmpty(value))
                    groupDict[field.Name] = value;
                continue;
            }

            switch (field.FieldType)
            {
                case "int":
                    dict[field.Name] = field.IntValue;
                    break;
                case "bool":
                    dict[field.Name] = field.BoolValue;
                    break;
                case "keyvalue":
                    var kvDict = new Dictionary<string, string>();
                    if (field.KeyValuePairs != null)
                    {
                        foreach (var kvp in field.KeyValuePairs)
                        {
                            if (!string.IsNullOrWhiteSpace(kvp.Key))
                                kvDict[kvp.Key] = kvp.Value;
                        }
                    }

                    if (kvDict.Count > 0)
                        dict[field.Name] = kvDict;
                    break;
                case "json":
                    // Blank = not set (SER-001); "" would not deserialize into a list.
                    if (string.IsNullOrWhiteSpace(field.StringValue))
                        break;
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<object>(field.StringValue);
                        if (parsed != null)
                            dict[field.Name] = parsed;
                    }
                    catch
                    {
                        // Keep invalid JSON visible to the user instead of dropping it.
                        dict[field.Name] = field.StringValue;
                    }
                    break;
                default:
                    if (!string.IsNullOrEmpty(field.StringValue))
                        dict[field.Name] = field.StringValue;
                    break;
            }
        }

        foreach (var (key, groupDict) in groups)
        {
            if (groupDict.Count > 0)
                dict[key] = groupDict;
        }

        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
    }

    internal static bool IsEntityType(Type type)
    {
        return type.IsClass && type != typeof(string)
            && type.GetProperties().Any(p =>
                Attribute.IsDefined(p, typeof(System.ComponentModel.DataAnnotations.KeyAttribute)));
    }

    internal static string FormatDisplayName(string paramName)
    {
        var result = new System.Text.StringBuilder();
        foreach (var c in paramName)
        {
            if (char.IsUpper(c) && result.Length > 0)
                result.Append(' ');
            result.Append(c);
        }
        return result.ToString();
    }

    private static List<KeyValuePairModel> ParseKeyValuePairs(JsonElement element)
    {
        var pairs = new List<KeyValuePairModel>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                pairs.Add(new KeyValuePairModel
                {
                    Key = prop.Name,
                    Value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText()
                });
            }
        }
        return pairs;
    }

    // The previous report's parameter values don't belong to a newly picked report (or to none).
    internal static string WithoutReportParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject obj
                && obj.Remove("ReportParameters"))
                return obj.ToJsonString();
        }
        catch (JsonException) { }
        return json;
    }

    private static string ExtractReportNameFromValues(Dictionary<string, JsonElement> values)
    {
        if (values.TryGetValue("ReportName", out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return null;
    }

    private static Dictionary<string, JsonElement> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
