using System.ComponentModel;
using System.Text.Json;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Editors;

[PropertyEditor(typeof(string), "JobParametersEditor", false)]
public class JobParametersPropertyEditor : BlazorPropertyEditorBase, IComplexViewItem
{
    private XafApplication application;
    private IObjectSpace objectSpace;

    public JobParametersPropertyEditor(Type objectType, IModelMemberViewItem model)
        : base(objectType, model) { }

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
    {
        this.objectSpace = objectSpace;
        this.application = application;
    }

    public override JobParametersFormModel ComponentModel =>
        (JobParametersFormModel)base.ComponentModel;

    protected override IComponentModel CreateComponentModel()
    {
        var model = new JobParametersFormModel();
        model.RawJsonChanged = EventCallback.Factory.Create<string>(this, json =>
        {
            var previousReportName = ExtractFieldValue(model.RawJson, "ReportName");
            model.RawJson = json;
            OnControlValueChanged();
            WriteValue();

            // If ReportName changed, refresh fields to auto-discover report parameters
            var newReportName = ExtractFieldValue(json, "ReportName");
            if (previousReportName != newReportName && !string.IsNullOrEmpty(newReportName))
            {
                RefreshFields(json);
            }
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

            // Determine field type based on DataSourceHint first, then CLR type
            if (param.DataSourceHint == "KeyValue" || param.DataSourceHint == "ReportParameters")
            {
                field.FieldType = "keyvalue";
            }
            else if (param.DataSourceHint != null && param.ParameterType == typeof(string))
            {
                field.FieldType = "dropdown";
                field.DropdownItems = ResolveDropdownItems(param.DataSourceHint);
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
                    case "keyvalue":
                        field.KeyValuePairs = ParseKeyValuePairs(element);
                        break;
                    case "json":
                        field.StringValue = element.GetRawText();
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

            // Auto-discover report parameters when hint is "ReportParameters"
            if (param.DataSourceHint == "ReportParameters" && field.FieldType == "keyvalue")
            {
                var reportName = ExtractReportNameFromValues(values);
                if (!string.IsNullOrEmpty(reportName))
                {
                    var discovered = DiscoverReportParameters(reportName);
                    if (discovered.Count > 0)
                    {
                        // Merge: discovered parameters as base, overlay with existing user values
                        var existingDict = field.KeyValuePairs
                            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                            .ToDictionary(p => p.Key, p => p.Value);

                        field.KeyValuePairs = discovered.Select(d => new KeyValuePairModel
                        {
                            Key = d.Key,
                            Value = existingDict.TryGetValue(d.Key, out var v) ? v : d.Value
                        }).ToList();
                    }
                }
            }

            fields.Add(field);
        }

        ComponentModel.Fields = fields;
    }

    private List<KeyValuePairModel> DiscoverReportParameters(string reportName)
    {
        try
        {
            var reportService = application.ServiceProvider.GetService<IReportExportService>();
            if (reportService == null) return new();

            using var report = reportService.LoadReport<ReportDataV2>(
                r => r.DisplayName == reportName);
            reportService.SetupReport(report);

            var pairs = new List<KeyValuePairModel>();
            foreach (DevExpress.XtraReports.Parameters.Parameter p in report.Parameters)
            {
                if (!p.Visible) continue;
                pairs.Add(new KeyValuePairModel
                {
                    Key = p.Name,
                    Value = p.Value?.ToString() ?? string.Empty
                });
            }
            return pairs;
        }
        catch
        {
            return new();
        }
    }

    private List<string> ResolveDropdownItems(string dataSourceHint)
    {
        // Static comma-separated values (e.g., "Pdf,Xlsx")
        if (dataSourceHint.Contains(','))
            return dataSourceHint.Split(',').ToList();

        // Database-backed lookups
        try
        {
            switch (dataSourceHint)
            {
                case "Reports":
                    using (var os = application.CreateObjectSpace(typeof(ReportDataV2)))
                    {
                        return os.GetObjectsQuery<ReportDataV2>()
                            .Select(r => r.DisplayName)
                            .Where(n => n != null)
                            .ToList();
                    }
                case "EmailTemplates":
                    using (var os = application.CreateObjectSpace(typeof(EmailTemplate)))
                    {
                        return os.GetObjectsQuery<EmailTemplate>()
                            .Select(t => t.Name)
                            .Where(n => n != null)
                            .ToList();
                    }
                default:
                    return new();
            }
        }
        catch
        {
            return new();
        }
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

    private string GetJobTypeName()
    {
        if (CurrentObject is JobDefinition jobDef)
            return jobDef.JobTypeName;
        return null;
    }

    private static string ExtractReportNameFromValues(Dictionary<string, JsonElement> values)
    {
        if (values.TryGetValue("ReportName", out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return null;
    }

    private static string ExtractFieldValue(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict != null && dict.TryGetValue(fieldName, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString();
        }
        catch { }
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
