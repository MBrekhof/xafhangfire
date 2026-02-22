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
            // Handle ReportParameters: emit individual typed fields instead of keyvalue
            if (param.DataSourceHint == "ReportParameters")
            {
                var reportName = ExtractReportNameFromValues(values);
                if (!string.IsNullOrEmpty(reportName))
                {
                    var discovered = DiscoverReportParameters(reportName);
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
                        {
                            var typedField = new ParameterFieldModel
                            {
                                Name = d.Name,
                                DisplayName = d.DisplayName,
                                GroupName = param.Name,
                            };

                            if (d.ClrType == typeof(DateTime) || d.ClrType == typeof(DateOnly))
                                typedField.FieldType = "date";
                            else if (d.ClrType == typeof(int))
                                typedField.FieldType = "int";
                            else if (d.ClrType == typeof(decimal) || d.ClrType == typeof(double) || d.ClrType == typeof(float))
                                typedField.FieldType = "decimal";
                            else if (d.ClrType == typeof(bool))
                                typedField.FieldType = "bool";
                            else if (IsEntityType(d.ClrType))
                            {
                                typedField.FieldType = "lookup";
                                typedField.LookupItems = LoadLookupItems(d.ClrType);
                            }
                            else
                                typedField.FieldType = "string";

                            var valueStr = existingValues.TryGetValue(d.Name, out var v) ? v : d.DefaultValue;
                            switch (typedField.FieldType)
                            {
                                case "date":
                                    typedField.DateTimeValue = DateTime.TryParse(valueStr, out var dt) ? dt : null;
                                    break;
                                case "int":
                                    typedField.IntValue = int.TryParse(valueStr, out var iv) ? iv : 0;
                                    break;
                                case "decimal":
                                    typedField.DecimalValue = decimal.TryParse(valueStr, out var dec) ? dec : 0;
                                    break;
                                case "bool":
                                    typedField.BoolValue = bool.TryParse(valueStr, out var b) && b;
                                    break;
                                case "lookup":
                                default:
                                    typedField.StringValue = valueStr;
                                    break;
                            }

                            fields.Add(typedField);
                        }
                        continue;
                    }
                }
            }

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

            fields.Add(field);
        }

        ComponentModel.Fields = fields;

        // Re-serialize fields to JSON so discovered params are persisted
        var updatedJson = SerializeFieldsToJson(fields);
        if (updatedJson != ComponentModel.RawJson)
        {
            ComponentModel.RawJson = updatedJson;
            OnControlValueChanged();
            WriteValue();
        }
    }

    private List<DiscoveredParameterInfo> DiscoverReportParameters(string reportName)
    {
        try
        {
            using var os = application.CreateObjectSpace(typeof(ReportDataV2));
            var reportData = os.GetObjectsQuery<ReportDataV2>()
                .Where(r => r.DisplayName == reportName)
                .FirstOrDefault();

            if (reportData == null)
                return new();

            // Primary: use ReportParametersObjectBase if registered
            if (!string.IsNullOrEmpty(reportData.ParametersObjectTypeName))
            {
                var paramsType = ResolveType(reportData.ParametersObjectTypeName);
                if (paramsType != null)
                {
                    var baseProps = typeof(DevExpress.ExpressApp.ReportsV2.ReportParametersObjectBase)
                        .GetProperties()
                        .Select(p => p.Name)
                        .ToHashSet();

                    return paramsType
                        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Where(p => !baseProps.Contains(p.Name) && p.CanRead && p.CanWrite)
                        .Select(p => new DiscoveredParameterInfo
                        {
                            Name = p.Name,
                            DisplayName = FormatDisplayName(p.Name),
                            ClrType = p.PropertyType,
                            DefaultValue = GetDefaultValueString(p.PropertyType)
                        })
                        .ToList();
                }
            }

            // Fallback: instantiate the report class and inspect XtraReport.Parameters
            if (!string.IsNullOrEmpty(reportData.PredefinedReportTypeName))
            {
                var reportType = ResolveType(reportData.PredefinedReportTypeName);
                if (reportType != null)
                {
                    using var report = (DevExpress.XtraReports.UI.XtraReport)Activator.CreateInstance(reportType);
                    var infos = new List<DiscoveredParameterInfo>();
                    foreach (DevExpress.XtraReports.Parameters.Parameter p in report.Parameters)
                    {
                        if (!p.Visible || string.IsNullOrEmpty(p.Name)) continue;
                        infos.Add(new DiscoveredParameterInfo
                        {
                            Name = p.Name,
                            DisplayName = p.Description ?? FormatDisplayName(p.Name),
                            ClrType = p.Type,
                            DefaultValue = p.Value?.ToString() ?? string.Empty
                        });
                    }
                    if (infos.Count > 0) return infos;
                }
            }

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

    private static bool IsEntityType(Type type)
    {
        return type.IsClass && type != typeof(string)
            && type.GetProperties().Any(p =>
                Attribute.IsDefined(p, typeof(System.ComponentModel.DataAnnotations.KeyAttribute)));
    }

    private static string GetDefaultPropertyName(Type type)
    {
        var attr = type.GetCustomAttributes(typeof(System.ComponentModel.DefaultPropertyAttribute), true)
            .OfType<System.ComponentModel.DefaultPropertyAttribute>()
            .FirstOrDefault();
        return attr?.Name ?? "Name";
    }

    private List<LookupItem> LoadLookupItems(Type entityType)
    {
        try
        {
            using var os = application.CreateObjectSpace(entityType);
            var keyProp = entityType.GetProperties()
                .First(p => Attribute.IsDefined(p, typeof(System.ComponentModel.DataAnnotations.KeyAttribute)));
            var displayPropName = GetDefaultPropertyName(entityType);
            var displayProp = entityType.GetProperty(displayPropName);

            var objects = os.GetObjects(entityType, null, false);
            var items = new List<LookupItem>();
            foreach (var obj in objects)
            {
                var id = keyProp.GetValue(obj)?.ToString() ?? string.Empty;
                var display = displayProp?.GetValue(obj)?.ToString() ?? id;
                items.Add(new LookupItem { Id = id, DisplayText = display });
            }
            return items.OrderBy(x => x.DisplayText).ToList();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[ParamEditor] Failed to load lookup items for {Type}", entityType.Name);
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

    private static string SerializeFieldsToJson(List<ParameterFieldModel> fields)
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
                default: // string, dropdown, json
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
