using System.Text.Json;

namespace xafhangfire.Blazor.Server.Editors;

internal static class JobParametersJsonSerializer
{
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

                groupDict[field.Name] = field.FieldType switch
                {
                    "date" => field.DateTimeValue?.ToString("yyyy-MM-dd") ?? string.Empty,
                    "int" => field.IntValue.ToString(),
                    "decimal" => field.DecimalValue.ToString(),
                    "bool" => field.BoolValue.ToString().ToLowerInvariant(),
                    _ => field.StringValue ?? string.Empty,
                };
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
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<object>(field.StringValue);
                        if (parsed != null)
                            dict[field.Name] = parsed;
                    }
                    catch
                    {
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
}
