using System.ComponentModel;
using System.Text.Json;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Editors;

[PropertyEditor(typeof(string), "JobParametersEditor", false)]
public class JobParametersPropertyEditor : BlazorPropertyEditorBase
{
    public JobParametersPropertyEditor(Type objectType, IModelMemberViewItem model)
        : base(objectType, model) { }

    public override JobParametersFormModel ComponentModel =>
        (JobParametersFormModel)base.ComponentModel;

    protected override IComponentModel CreateComponentModel()
    {
        var model = new JobParametersFormModel();
        model.RawJsonChanged = EventCallback.Factory.Create<string>(this, json =>
        {
            model.RawJson = json;
            OnControlValueChanged();
            WriteValue();
        });
        return model;
    }

    protected override void OnCurrentObjectChanged()
    {
        base.OnCurrentObjectChanged();
        UnsubscribeFromPropertyChanged();
        SubscribeToPropertyChanged();
    }

    protected override void ReadValueCore()
    {
        base.ReadValueCore();
        var json = (string)PropertyValue ?? string.Empty;
        ComponentModel.RawJson = json;
        RefreshFields(json);
    }

    protected override object GetControlValueCore() => ComponentModel.RawJson;

    protected override void Dispose(bool disposing)
    {
        UnsubscribeFromPropertyChanged();
        base.Dispose(disposing);
    }

    private void SubscribeToPropertyChanged()
    {
        if (CurrentObject is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnObjectPropertyChanged;
    }

    private void UnsubscribeFromPropertyChanged()
    {
        if (CurrentObject is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnObjectPropertyChanged;
    }

    private void OnObjectPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobDefinition.JobTypeName))
        {
            var json = ComponentModel.RawJson;
            RefreshFields(json);
        }
    }

    private void RefreshFields(string json)
    {
        var jobTypeName = GetJobTypeName();
        var metadata = jobTypeName != null
            ? CommandMetadataProvider.GetMetadata(jobTypeName)
            : null;

        if (metadata == null)
        {
            ComponentModel.ShowRawEditor = true;
            ComponentModel.Fields = new();
            return;
        }

        ComponentModel.ShowRawEditor = false;
        var values = ParseJson(json);
        var fields = new List<ParameterFieldModel>();

        foreach (var param in metadata)
        {
            var field = new ParameterFieldModel
            {
                Name = param.Name,
                DisplayName = FormatDisplayName(param.Name),
                IsRequired = param.IsRequired,
            };

            // Determine field type
            if (param.ParameterType == typeof(int))
                field.FieldType = "int";
            else if (param.ParameterType == typeof(bool))
                field.FieldType = "bool";
            else if (param.ParameterType == typeof(string))
                field.FieldType = "string";
            else
                field.FieldType = "json"; // Dictionary, IReadOnlyList, etc.

            // Set current value from JSON
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
                    case "json":
                        field.StringValue = element.GetRawText();
                        break;
                    default:
                        field.StringValue = element.ValueKind == JsonValueKind.String
                            ? element.GetString() ?? string.Empty
                            : element.GetRawText();
                        break;
                }
            }
            else if (param.DefaultValue != null)
            {
                // Use default value
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

            fields.Add(field);
        }

        ComponentModel.Fields = fields;
    }

    private string GetJobTypeName()
    {
        if (CurrentObject is JobDefinition jobDef)
            return jobDef.JobTypeName;
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

    private static string FormatDisplayName(string paramName)
    {
        // "DelaySeconds" -> "Delay Seconds"
        var result = new System.Text.StringBuilder();
        foreach (var c in paramName)
        {
            if (char.IsUpper(c) && result.Length > 0)
                result.Append(' ');
            result.Append(c);
        }
        return result.ToString();
    }
}
