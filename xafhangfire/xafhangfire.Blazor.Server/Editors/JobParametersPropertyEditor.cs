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
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Editors;

[PropertyEditor(typeof(string), "JobParametersEditor", false)]
public class JobParametersPropertyEditor : BlazorPropertyEditorBase, IComplexViewItem
{
    private XafApplication application;
    private IObjectSpace objectSpace;
    private ILogger logger;

    public JobParametersPropertyEditor(Type objectType, IModelMemberViewItem model)
        : base(objectType, model) { }

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
    {
        this.objectSpace = objectSpace;
        this.application = application;
        this.logger = application.ServiceProvider.GetService<ILoggerFactory>()
            ?.CreateLogger<JobParametersPropertyEditor>();
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
        logger?.LogInformation("[ParamEditor] RefreshFields called. JobTypeName='{JobType}', json='{Json}'",
            jobTypeName ?? "(null)", json?.Length > 200 ? json[..200] + "..." : json);

        var metadata = jobTypeName != null
            ? CommandMetadataProvider.GetMetadata(jobTypeName)
            : null;

        if (metadata == null)
        {
            logger?.LogInformation("[ParamEditor] No metadata for '{JobType}', showing raw editor", jobTypeName);
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
                logger?.LogInformation("[ParamEditor] ReportParameters hint for field '{Field}', reportName='{ReportName}', existing KV count={Count}",
                    param.Name, reportName ?? "(null)", field.KeyValuePairs?.Count ?? 0);

                if (!string.IsNullOrEmpty(reportName))
                {
                    var discovered = DiscoverReportParameters(reportName);
                    logger?.LogInformation("[ParamEditor] Discovered {Count} params for '{ReportName}': [{Params}]",
                        discovered.Count, reportName,
                        string.Join(", ", discovered.Select(d => $"{d.Key}={d.Value}")));

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

                        logger?.LogInformation("[ParamEditor] After merge: [{Merged}]",
                            string.Join(", ", field.KeyValuePairs.Select(p => $"{p.Key}={p.Value}")));
                    }
                }
            }

            fields.Add(field);
        }

        ComponentModel.Fields = fields;

        // Re-serialize fields to JSON so discovered params are persisted
        var updatedJson = SerializeFieldsToJson(fields);
        if (updatedJson != ComponentModel.RawJson)
        {
            logger?.LogInformation("[ParamEditor] Updating JSON after discovery. Old length={OldLen}, New length={NewLen}",
                ComponentModel.RawJson?.Length ?? 0, updatedJson.Length);
            ComponentModel.RawJson = updatedJson;
            OnControlValueChanged();
            WriteValue();
        }

        logger?.LogInformation("[ParamEditor] RefreshFields complete. {Count} fields assigned. KV fields: [{KvSummary}]",
            fields.Count,
            string.Join("; ", fields.Where(f => f.FieldType == "keyvalue")
                .Select(f => $"{f.Name}=[{string.Join(",", f.KeyValuePairs?.Select(p => $"{p.Key}:{p.Value}") ?? Array.Empty<string>())}]")));
    }

    private List<KeyValuePairModel> DiscoverReportParameters(string reportName)
    {
        try
        {
            using var os = application.CreateObjectSpace(typeof(ReportDataV2));
            var reportData = os.GetObjectsQuery<ReportDataV2>()
                .Where(r => r.DisplayName == reportName)
                .FirstOrDefault();

            if (reportData == null)
            {
                logger?.LogWarning("[ParamEditor.Discover] No ReportDataV2 found for '{ReportName}'", reportName);
                return new();
            }

            logger?.LogInformation("[ParamEditor.Discover] Found report '{ReportName}': ParametersObjectTypeName='{ParamsType}', PredefinedReportTypeName='{ReportType}'",
                reportName, reportData.ParametersObjectTypeName ?? "(null)", reportData.PredefinedReportTypeName ?? "(null)");

            // Primary: use ReportParametersObjectBase if registered
            if (!string.IsNullOrEmpty(reportData.ParametersObjectTypeName))
            {
                var paramsType = ResolveType(reportData.ParametersObjectTypeName);
                logger?.LogInformation("[ParamEditor.Discover] Resolved ParametersObjectType: {Resolved}",
                    paramsType?.FullName ?? "FAILED TO RESOLVE");

                if (paramsType != null)
                {
                    var baseProps = typeof(DevExpress.ExpressApp.ReportsV2.ReportParametersObjectBase)
                        .GetProperties()
                        .Select(p => p.Name)
                        .ToHashSet();

                    var allProps = paramsType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    logger?.LogInformation("[ParamEditor.Discover] All public props on {Type}: [{Props}]",
                        paramsType.Name,
                        string.Join(", ", allProps.Select(p => $"{p.Name}({p.PropertyType.Name})")));
                    logger?.LogInformation("[ParamEditor.Discover] Base class props to exclude: [{BaseProps}]",
                        string.Join(", ", baseProps));

                    var result = allProps
                        .Where(p => !baseProps.Contains(p.Name) && p.CanRead && p.CanWrite)
                        .Select(p => new KeyValuePairModel
                        {
                            Key = p.Name,
                            Value = GetDefaultValueString(p.PropertyType)
                        })
                        .ToList();

                    logger?.LogInformation("[ParamEditor.Discover] Returning {Count} params via ParametersObject: [{Params}]",
                        result.Count, string.Join(", ", result.Select(r => $"{r.Key}={r.Value}")));
                    return result;
                }
            }

            // Fallback: instantiate the report class and inspect XtraReport.Parameters
            if (!string.IsNullOrEmpty(reportData.PredefinedReportTypeName))
            {
                var reportType = ResolveType(reportData.PredefinedReportTypeName);
                logger?.LogInformation("[ParamEditor.Discover] Fallback — resolved report type: {Resolved}",
                    reportType?.FullName ?? "FAILED TO RESOLVE");

                if (reportType != null)
                {
                    using var report = (DevExpress.XtraReports.UI.XtraReport)Activator.CreateInstance(reportType);
                    var pairs = new List<KeyValuePairModel>();
                    foreach (DevExpress.XtraReports.Parameters.Parameter p in report.Parameters)
                    {
                        logger?.LogInformation("[ParamEditor.Discover] Fallback param: Name='{Name}', Visible={Visible}, Type={Type}, Value='{Value}'",
                            p.Name ?? "(null)", p.Visible, p.Type?.Name ?? "(null)", p.Value);
                        if (!p.Visible || string.IsNullOrEmpty(p.Name)) continue;
                        pairs.Add(new KeyValuePairModel
                        {
                            Key = p.Name,
                            Value = p.Value?.ToString() ?? string.Empty
                        });
                    }
                    if (pairs.Count > 0) return pairs;
                }
            }

            logger?.LogWarning("[ParamEditor.Discover] No parameters found for '{ReportName}'", reportName);
            return new();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[ParamEditor.Discover] Exception discovering params for '{ReportName}'", reportName);
            return new();
        }
    }

    private static Type ResolveType(string typeName)
    {
        return Type.GetType(typeName)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(t => t != null);
    }

    private static string GetDefaultValueString(Type type)
    {
        if (type == typeof(DateTime))
            return DateTime.Now.ToString("yyyy-MM-dd");
        if (type == typeof(DateOnly))
            return DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (type == typeof(int) || type == typeof(decimal) || type == typeof(double))
            return "0";
        if (type == typeof(bool))
            return "false";
        return string.Empty;
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

    private static string SerializeFieldsToJson(List<ParameterFieldModel> fields)
    {
        var dict = new Dictionary<string, object>();
        foreach (var field in fields)
        {
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
                default: // string, dropdown, json
                    if (!string.IsNullOrEmpty(field.StringValue))
                        dict[field.Name] = field.StringValue;
                    break;
            }
        }
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
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
