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
            // ReportName changed (or cleared) in the typed form: drop the old report's values,
            // then rediscover the new report's parameters. The raw editor keeps what was typed.
            var reportChanged = !model.ShowRawEditor
                && ExtractFieldValue(model.RawJson, "ReportName") != ExtractFieldValue(json, "ReportName");
            if (reportChanged)
                json = JobParametersJsonSerializer.WithoutReportParameters(json);

            model.RawJson = json;
            OnControlValueChanged();
            WriteValue();

            if (reportChanged)
                RefreshFields(json, persist: true);
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
        // Opening a record must never rewrite it (SER-000).
        RefreshFields(json, persist: false);
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
            RefreshFields(json, persist: true);
        }
    }

    private void RefreshFields(string json, bool persist)
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
        var fields = JobParametersJsonSerializer.LoadFields(
            json, metadata, DiscoverReportParameters, LoadLookupItems, ResolveDropdownItems);
        ComponentModel.Fields = fields;

        // Only user actions (JobTypeName / ReportName change) write back, so newly discovered
        // parameters land in the JSON. Loading never does: any value the typed fields cannot
        // represent would otherwise be rewritten just by opening the record (SER-000).
        if (!persist)
            return;

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
                            DisplayName = JobParametersJsonSerializer.FormatDisplayName(p.Name),
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
                            DisplayName = p.Description ?? JobParametersJsonSerializer.FormatDisplayName(p.Name),
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
        if (type == typeof(int) || type == typeof(decimal) || type == typeof(double))
            return "0";
        if (type == typeof(bool))
            return "false";
        return string.Empty;
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

    private string GetJobTypeName()
    {
        if (CurrentObject is JobDefinition jobDef)
            return jobDef.JobTypeName;
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

    private static string SerializeFieldsToJson(List<ParameterFieldModel> fields)
        => JobParametersJsonSerializer.SerializeFields(fields);
}
