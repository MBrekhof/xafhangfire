using DevExpress.ExpressApp.Blazor.Components.Models;
using Microsoft.AspNetCore.Components;

namespace xafhangfire.Blazor.Server.Editors;

public class JobParametersFormModel : ComponentModelBase
{
    public List<ParameterFieldModel> Fields
    {
        get => GetPropertyValue<List<ParameterFieldModel>>() ?? new();
        set => SetPropertyValue(value);
    }

    public bool ShowRawEditor
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    public string RawJson
    {
        get => GetPropertyValue<string>() ?? string.Empty;
        set => SetPropertyValue(value);
    }

    public EventCallback<string> RawJsonChanged
    {
        get => GetPropertyValue<EventCallback<string>>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(JobParametersForm);
}

public class ParameterFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FieldType { get; set; } = "string"; // string, int, bool, json, keyvalue, dropdown
    public bool IsRequired { get; set; }
    public string StringValue { get; set; } = string.Empty;
    public int IntValue { get; set; }
    public bool BoolValue { get; set; }
    public List<KeyValuePairModel> KeyValuePairs { get; set; } = new();
    public List<string> DropdownItems { get; set; } = new();
    public List<LookupItem> LookupItems { get; set; } = new();
    public DateTime? DateTimeValue { get; set; }
    public decimal DecimalValue { get; set; }
    public string GroupName { get; set; }
}

public class DiscoveredParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Type ClrType { get; set; } = typeof(string);
    public string DefaultValue { get; set; } = string.Empty;
}

public class LookupItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
}

public class KeyValuePairModel
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
