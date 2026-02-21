# Job Definition UI Improvements — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve the JobDefinition editing experience with a JobTypeName dropdown, live field refresh, key-value pair editor for Dictionary parameters, ReportName dropdown from the database, DateTime display formats, and read-only system fields.

**Architecture:** Extend existing custom property editor pattern (BlazorPropertyEditorBase + ComponentModelBase + Razor). Add a second property editor for JobTypeName (DxComboBox). Add a ViewController to refresh computed properties on change. Extend CommandParameterMetadata with DataSourceHint for database-backed dropdowns. Add "keyvalue" and "dropdown" field types to the parameter form.

**Tech Stack:** XAF Blazor property editors, DevExpress Blazor DxComboBox/DxTextBox, IObjectSpaceFactory for report name lookup, ModelDefault attributes for DateTime formatting.

---

### Task 1: DateTime display formats + read-only system fields

Quick attribute changes across two entity files. No new code, no tests needed.

**Files:**
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs:57-69`
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/JobExecutionRecord.cs:22-30`

**Step 1: Add ModelDefault display format to JobDefinition timestamps**

In `JobDefinition.cs`, add `[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]` to `LastRunUtc` and `NextRunUtc`. Add `[EditorBrowsable(EditorBrowsableState.Never)]` to `LastRunMessage` to make it read-only. Add `using DevExpress.ExpressApp.Model;` at the top.

```csharp
// Add to usings:
using DevExpress.ExpressApp.Model;

// Replace LastRunUtc (line 55-57):
[VisibleInDetailView(true), VisibleInListView(true)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
public virtual DateTime? LastRunUtc { get; set; }

// Replace NextRunUtc (line 59-61):
[VisibleInDetailView(true), VisibleInListView(true)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
public virtual DateTime? NextRunUtc { get; set; }

// Replace LastRunMessage (line 67-69):
[FieldSize(FieldSizeAttribute.Unlimited)]
[VisibleInListView(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public virtual string LastRunMessage { get; set; }
```

**Step 2: Add ModelDefault display format to JobExecutionRecord timestamps**

In `JobExecutionRecord.cs`, add `[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]` to `StartedUtc` and `CompletedUtc`. Add `[EditorBrowsable(EditorBrowsableState.Never)]` to `ErrorMessage` and `ParametersJson`. Add `using DevExpress.ExpressApp.Model;` at the top.

```csharp
// Add to usings:
using DevExpress.ExpressApp.Model;

// Replace StartedUtc (line 22):
[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
public virtual DateTime StartedUtc { get; set; }

// Replace CompletedUtc (line 24):
[ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
public virtual DateTime? CompletedUtc { get; set; }

// Replace ErrorMessage (line 28-30):
[FieldSize(FieldSizeAttribute.Unlimited)]
[VisibleInListView(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public virtual string ErrorMessage { get; set; }

// Replace ParametersJson (line 38-40):
[FieldSize(FieldSizeAttribute.Unlimited)]
[VisibleInListView(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public virtual string ParametersJson { get; set; }
```

**Step 3: Build to verify**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs xafhangfire/xafhangfire.Module/BusinessObjects/JobExecutionRecord.cs
git commit -m "fix: add DateTime display formats and make system fields read-only"
```

---

### Task 2: JobTypeName dropdown property editor

**Files:**
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBoxModel.cs`
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBox.razor`
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNamePropertyEditor.cs`
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs:33-34`

**Step 1: Create the component model**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBoxModel.cs
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
```

**Step 2: Create the Blazor component**

```razor
@* xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBox.razor *@
@using DevExpress.Blazor

<DxComboBox Data="@Items"
            Value="@Value"
            ValueChanged="@((string v) => OnValueChanged(v))"
            AllowUserInput="true"
            ClearButtonDisplayMode="DataEditorClearButtonDisplayMode.Auto"
            NullText="Select a command type..."
            CssClass="w-100"
            ReadOnly="@ReadOnly" />

@code {
    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public List<string> Items { get; set; } = new();

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }

    private async Task OnValueChanged(string value)
    {
        await ValueChanged.InvokeAsync(value ?? string.Empty);
    }
}
```

**Step 3: Create the property editor**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNamePropertyEditor.cs
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Editors;

[PropertyEditor(typeof(string), "JobTypeNameEditor", false)]
public class JobTypeNamePropertyEditor : BlazorPropertyEditorBase
{
    public JobTypeNamePropertyEditor(Type objectType, IModelMemberViewItem model)
        : base(objectType, model) { }

    public override JobTypeNameComboBoxModel ComponentModel =>
        (JobTypeNameComboBoxModel)base.ComponentModel;

    protected override IComponentModel CreateComponentModel()
    {
        var model = new JobTypeNameComboBoxModel();
        model.Items = CommandMetadataProvider.GetRegisteredTypeNames().ToList();
        model.ValueChanged = EventCallback.Factory.Create<string>(this, value =>
        {
            model.Value = value;
            OnControlValueChanged();
            WriteValue();
        });
        return model;
    }

    protected override void ReadValueCore()
    {
        base.ReadValueCore();
        ComponentModel.Value = (string)PropertyValue ?? string.Empty;
        ComponentModel.ReadOnly = !AllowEdit;
    }

    protected override object GetControlValueCore() => ComponentModel.Value;
}
```

**Step 4: Add EditorAlias to JobTypeName property**

In `JobDefinition.cs`, add the `[EditorAlias("JobTypeNameEditor")]` attribute to `JobTypeName`:

```csharp
// Replace line 33-34:
[Required]
[EditorAlias("JobTypeNameEditor")]
public virtual string JobTypeName { get; set; } = string.Empty;
```

Add `using DevExpress.ExpressApp.Editors;` to the usings if not already present.

**Step 5: Build**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 6: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBoxModel.cs xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBox.razor xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNamePropertyEditor.cs xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs
git commit -m "feat: add JobTypeName dropdown populated from registered command types"
```

---

### Task 3: Live field refresh ViewController

When `CronExpression` changes, the computed `CronDescription` and `NextScheduledRuns` don't update because they're `[NotMapped]` and not tracked by EF Core. A ViewController fixes this by listening for changes and refreshing the view.

**Files:**
- Create: `xafhangfire/xafhangfire.Blazor.Server/Controllers/JobDefinitionRefreshController.cs`

**Step 1: Create the ViewController**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/Controllers/JobDefinitionRefreshController.cs
using DevExpress.ExpressApp;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Controllers;

public sealed class JobDefinitionRefreshController : ObjectViewController<DetailView, JobDefinition>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
        base.OnDeactivated();
    }

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        if (e.Object is not JobDefinition || e.PropertyName == null)
            return;

        if (e.PropertyName == nameof(JobDefinition.CronExpression))
        {
            // Force XAF to re-read the computed [NotMapped] properties
            View.FindItem(nameof(JobDefinition.CronDescription))?.Refresh();
            View.FindItem(nameof(JobDefinition.NextScheduledRuns))?.Refresh();
        }
    }
}
```

**Step 2: Build**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/Controllers/JobDefinitionRefreshController.cs
git commit -m "feat: live refresh of cron description and next runs on CronExpression change"
```

---

### Task 4: Extend CommandParameterMetadata with DataSourceHint

Add a `DataSourceHint` to metadata so the property editor knows which parameters should render as dropdowns with database-backed data.

**Files:**
- Modify: `xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs`
- Modify: `xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs`
- Modify: `xafhangfire/xafhangfire.Jobs.Tests/CommandMetadataProviderTests.cs`

**Step 1: Write failing tests for DataSourceHint**

Add to `CommandMetadataProviderTests.cs`:

```csharp
[Fact]
public void GetMetadata_GenerateReportCommand_ReportNameHasReportsHint()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(GenerateReportCommand));

    var reportName = metadata!.First(m => m.Name == "ReportName");
    reportName.DataSourceHint.Should().Be("Reports");
}

[Fact]
public void GetMetadata_GenerateReportCommand_OutputFormatHasStaticHint()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(GenerateReportCommand));

    var outputFormat = metadata!.First(m => m.Name == "OutputFormat");
    outputFormat.DataSourceHint.Should().Be("Pdf,Xlsx");
}

[Fact]
public void GetMetadata_SendReportEmailCommand_ReportNameHasReportsHint()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(SendReportEmailCommand));

    var reportName = metadata!.First(m => m.Name == "ReportName");
    reportName.DataSourceHint.Should().Be("Reports");
}

[Fact]
public void GetMetadata_SendMailMergeCommand_TemplateNameHasEmailTemplatesHint()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(SendMailMergeCommand));

    var templateName = metadata!.First(m => m.Name == "TemplateName");
    templateName.DataSourceHint.Should().Be("EmailTemplates");
}

[Fact]
public void GetMetadata_DemoLogCommand_NoDataSourceHints()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(DemoLogCommand));

    metadata!.Should().AllSatisfy(m => m.DataSourceHint.Should().BeNull());
}

[Fact]
public void GetMetadata_GenerateReportCommand_ReportParametersHasKeyValueHint()
{
    var metadata = CommandMetadataProvider.GetMetadata(nameof(GenerateReportCommand));

    var reportParams = metadata!.First(m => m.Name == "ReportParameters");
    reportParams.DataSourceHint.Should().Be("KeyValue");
}
```

**Step 2: Run tests — verify they fail**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj --filter CommandMetadataProviderTests
```
Expected: FAIL (DataSourceHint property doesn't exist).

**Step 3: Add DataSourceHint to CommandParameterMetadata**

Replace `xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs`:

```csharp
#nullable enable

namespace xafhangfire.Jobs;

public sealed record CommandParameterMetadata(
    string Name,
    Type ParameterType,
    bool IsRequired,
    object? DefaultValue,
    string? DataSourceHint = null);
```

**Step 4: Add DataSourceHint resolution to CommandMetadataProvider**

Replace `xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs`:

```csharp
#nullable enable

using System.Reflection;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs;

public static class CommandMetadataProvider
{
    private static readonly Dictionary<string, Type> CommandTypes = new()
    {
        [nameof(DemoLogCommand)] = typeof(DemoLogCommand),
        [nameof(ListUsersCommand)] = typeof(ListUsersCommand),
        [nameof(GenerateReportCommand)] = typeof(GenerateReportCommand),
        [nameof(SendEmailCommand)] = typeof(SendEmailCommand),
        [nameof(SendReportEmailCommand)] = typeof(SendReportEmailCommand),
        [nameof(SendMailMergeCommand)] = typeof(SendMailMergeCommand),
    };

    // Maps (CommandType, ParameterName) -> DataSourceHint
    private static readonly Dictionary<(string Command, string Param), string> DataSourceHints = new()
    {
        [("GenerateReportCommand", "ReportName")] = "Reports",
        [("GenerateReportCommand", "OutputFormat")] = "Pdf,Xlsx",
        [("GenerateReportCommand", "ReportParameters")] = "KeyValue",
        [("SendReportEmailCommand", "ReportName")] = "Reports",
        [("SendReportEmailCommand", "OutputFormat")] = "Pdf,Xlsx",
        [("SendReportEmailCommand", "ReportParameters")] = "KeyValue",
        [("SendMailMergeCommand", "TemplateName")] = "EmailTemplates",
        [("SendEmailCommand", "AttachmentPaths")] = "KeyValue",
    };

    public static IReadOnlyList<CommandParameterMetadata>? GetMetadata(string jobTypeName)
    {
        if (!CommandTypes.TryGetValue(jobTypeName, out var commandType))
            return null;

        var ctor = commandType.GetConstructors().FirstOrDefault();
        if (ctor == null) return null;

        return ctor.GetParameters().Select(p =>
        {
            DataSourceHints.TryGetValue((jobTypeName, p.Name!), out var hint);

            return new CommandParameterMetadata(
                Name: p.Name!,
                ParameterType: p.ParameterType,
                IsRequired: !p.HasDefaultValue,
                DefaultValue: p.HasDefaultValue ? p.DefaultValue : null,
                DataSourceHint: hint
            );
        }).ToList();
    }

    public static IReadOnlyList<string> GetRegisteredTypeNames() =>
        CommandTypes.Keys.ToList();
}
```

**Step 5: Run tests — verify they pass**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj --filter CommandMetadataProviderTests
```
Expected: All pass.

**Step 6: Build full solution**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 7: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs xafhangfire/xafhangfire.Jobs.Tests/CommandMetadataProviderTests.cs
git commit -m "feat: add DataSourceHint to CommandParameterMetadata for dropdown support"
```

---

### Task 5: Key-value pair editor + dropdown support in parameter form

Extend the existing parameter form to handle `"keyvalue"` field type (add/remove rows) and `"dropdown"` field type (DxComboBox from static or database-backed lists).

**Files:**
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs`
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor`
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs`

**Step 1: Extend ParameterFieldModel with key-value and dropdown support**

Replace `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs`:

```csharp
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
}

public class KeyValuePairModel
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
```

**Step 2: Update the Blazor form component**

Replace `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor`:

```razor
@using DevExpress.Blazor

@if (ShowRawEditor)
{
    <DxMemo Value="@RawJson"
            ValueChanged="@((string v) => OnRawJsonChanged(v))"
            Rows="6" />
}
else
{
    @foreach (var field in Fields)
    {
        <div class="mb-2">
            <label class="xaf-field-caption">@field.DisplayName@(field.IsRequired ? " *" : "")</label>
            @switch (field.FieldType)
            {
                case "int":
                    <DxSpinEdit Value="@field.IntValue"
                                ValueChanged="@((int v) => OnIntChanged(field, v))"
                                CssClass="w-100" />
                    break;
                case "bool":
                    <DxCheckBox Checked="@field.BoolValue"
                                CheckedChanged="@((bool v) => OnBoolChanged(field, v))" />
                    break;
                case "dropdown":
                    <DxComboBox Data="@field.DropdownItems"
                                Value="@field.StringValue"
                                ValueChanged="@((string v) => OnStringChanged(field, v))"
                                AllowUserInput="true"
                                ClearButtonDisplayMode="DataEditorClearButtonDisplayMode.Auto"
                                NullText="Select..."
                                CssClass="w-100" />
                    break;
                case "keyvalue":
                    <div class="border rounded p-2">
                        @foreach (var kvp in field.KeyValuePairs)
                        {
                            <div class="d-flex gap-2 mb-1 align-items-center">
                                <DxTextBox Value="@kvp.Key"
                                           ValueChanged="@((string v) => OnKvKeyChanged(field, kvp, v))"
                                           NullText="Key"
                                           CssClass="flex-grow-1" />
                                <DxTextBox Value="@kvp.Value"
                                           ValueChanged="@((string v) => OnKvValueChanged(field, kvp, v))"
                                           NullText="Value"
                                           CssClass="flex-grow-1" />
                                <DxButton RenderStyle="ButtonRenderStyle.Danger"
                                          RenderStyleMode="ButtonRenderStyleMode.Outline"
                                          IconCssClass="oi oi-trash"
                                          Click="@(() => OnKvRemove(field, kvp))" />
                            </div>
                        }
                        <DxButton Text="Add Parameter"
                                  RenderStyle="ButtonRenderStyle.Secondary"
                                  RenderStyleMode="ButtonRenderStyleMode.Outline"
                                  IconCssClass="oi oi-plus"
                                  Click="@(() => OnKvAdd(field))"
                                  CssClass="mt-1" />
                    </div>
                    break;
                case "json":
                    <DxMemo Value="@field.StringValue"
                            ValueChanged="@((string v) => OnStringChanged(field, v))"
                            Rows="3" CssClass="w-100" />
                    break;
                default:
                    <DxTextBox Value="@field.StringValue"
                               ValueChanged="@((string v) => OnStringChanged(field, v))"
                               CssClass="w-100" />
                    break;
            }
        </div>
    }
}

@code {
    [Parameter]
    public List<ParameterFieldModel> Fields { get; set; } = new();

    [Parameter]
    public bool ShowRawEditor { get; set; }

    [Parameter]
    public string RawJson { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> RawJsonChanged { get; set; }

    private async Task OnRawJsonChanged(string value)
    {
        await RawJsonChanged.InvokeAsync(value);
    }

    private async Task OnStringChanged(ParameterFieldModel field, string value)
    {
        field.StringValue = value;
        await NotifyJsonChanged();
    }

    private async Task OnIntChanged(ParameterFieldModel field, int value)
    {
        field.IntValue = value;
        await NotifyJsonChanged();
    }

    private async Task OnBoolChanged(ParameterFieldModel field, bool value)
    {
        field.BoolValue = value;
        await NotifyJsonChanged();
    }

    private async Task OnKvKeyChanged(ParameterFieldModel field, KeyValuePairModel kvp, string value)
    {
        kvp.Key = value;
        await NotifyJsonChanged();
    }

    private async Task OnKvValueChanged(ParameterFieldModel field, KeyValuePairModel kvp, string value)
    {
        kvp.Value = value;
        await NotifyJsonChanged();
    }

    private async Task OnKvRemove(ParameterFieldModel field, KeyValuePairModel kvp)
    {
        field.KeyValuePairs.Remove(kvp);
        await NotifyJsonChanged();
    }

    private async Task OnKvAdd(ParameterFieldModel field)
    {
        field.KeyValuePairs.Add(new KeyValuePairModel());
        await NotifyJsonChanged();
    }

    private async Task NotifyJsonChanged()
    {
        var json = SerializeFields();
        await RawJsonChanged.InvokeAsync(json);
    }

    private string SerializeFields()
    {
        var dict = new Dictionary<string, object>();
        foreach (var field in Fields)
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
                    foreach (var kvp in field.KeyValuePairs)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key))
                            kvDict[kvp.Key] = kvp.Value;
                    }
                    if (kvDict.Count > 0)
                        dict[field.Name] = kvDict;
                    break;
                case "json":
                    try
                    {
                        dict[field.Name] = System.Text.Json.JsonSerializer.Deserialize<object>(field.StringValue);
                    }
                    catch
                    {
                        dict[field.Name] = field.StringValue;
                    }
                    break;
                default: // string, dropdown
                    if (!string.IsNullOrEmpty(field.StringValue))
                        dict[field.Name] = field.StringValue;
                    break;
            }
        }
        return System.Text.Json.JsonSerializer.Serialize(dict,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}
```

**Step 3: Update the property editor to use DataSourceHint**

Replace `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs`:

```csharp
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

            // Determine field type based on DataSourceHint first, then CLR type
            if (param.DataSourceHint == "KeyValue")
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
    }

    private List<string> ResolveDropdownItems(string dataSourceHint)
    {
        // Static comma-separated values (e.g., "Pdf,Xlsx")
        if (dataSourceHint.Contains(','))
            return dataSourceHint.Split(',').ToList();

        // Database-backed lookups
        try
        {
            var objectSpace = application?.CreateObjectSpace(typeof(ReportDataV2));
            if (objectSpace == null) return new();

            switch (dataSourceHint)
            {
                case "Reports":
                    return objectSpace
                        .GetObjectsQuery<ReportDataV2>()
                        .Select(r => r.DisplayName)
                        .Where(n => n != null)
                        .ToList();
                case "EmailTemplates":
                    return objectSpace
                        .GetObjectsQuery<EmailTemplate>()
                        .Select(t => t.Name)
                        .Where(n => n != null)
                        .ToList();
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
```

Note: The `ResolveDropdownItems` method needs access to `IObjectSpaceFactory`. In XAF Blazor property editors, `BlazorPropertyEditorBase` has access to the `Application` via the `application` field. We use `application.CreateObjectSpace()` to query the database. If `application` is not accessible as a field, we'll access it via `Application` property. Check during implementation — if `application` field is not available, use the `ObjectSpace` from `View` instead.

**Step 4: Build**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 5: Run all tests**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```
Expected: All pass.

**Step 6: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs
git commit -m "feat: add key-value pair editor and dropdown support for job parameters"
```

---

### Task 6: Update docs and final verification

**Files:**
- Modify: `TODO.md`

**Step 1: Build entire solution**

```bash
dotnet build xafhangfire.slnx
```

**Step 2: Run all tests**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```

**Step 3: Update TODO.md**

Add session 9 status and completed items for: JobTypeName dropdown, live field refresh, key-value pair editor, ReportName dropdown, DateTime formats, read-only system fields.

**Step 4: Commit**

```bash
git add TODO.md
git commit -m "docs: update TODO with job UI improvements (session 9)"
```
