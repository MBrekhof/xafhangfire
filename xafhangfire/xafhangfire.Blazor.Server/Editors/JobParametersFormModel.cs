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
    public string FieldType { get; set; } = "string"; // string, int, bool, json
    public bool IsRequired { get; set; }
    public string StringValue { get; set; } = string.Empty;
    public int IntValue { get; set; }
    public bool BoolValue { get; set; }
}
