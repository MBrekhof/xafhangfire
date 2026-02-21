using DevExpress.ExpressApp.Blazor.Components.Models;
using Microsoft.AspNetCore.Components;

namespace xafhangfire.Blazor.Server.Editors;

public class JobTypeNameComboBoxModel : ComponentModelBase
{
    public string Value
    {
        get => GetPropertyValue<string>() ?? string.Empty;
        set => SetPropertyValue(value);
    }

    public List<string> Items
    {
        get => GetPropertyValue<List<string>>() ?? new();
        set => SetPropertyValue(value);
    }

    public EventCallback<string> ValueChanged
    {
        get => GetPropertyValue<EventCallback<string>>();
        set => SetPropertyValue(value);
    }

    public bool ReadOnly
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(JobTypeNameComboBox);
}
