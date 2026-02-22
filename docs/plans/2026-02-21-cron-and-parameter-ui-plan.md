# Cron Visualization & Rich Parameter UI — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add human-readable cron descriptions with next-run previews, and replace raw JSON parameter editing with dynamic typed forms driven by command record reflection.

**Architecture:** Computed `[NotMapped]` properties on JobDefinition for cron display. CommandMetadataProvider reflects on command records to produce parameter metadata. Custom XAF Blazor property editor renders typed form fields for ParametersJson.

**Tech Stack:** Cronos (next occurrences), CronExpressionDescriptor (human-readable text), XAF Blazor custom property editor, System.Reflection.

---

### Task 1: Add NuGet packages

**Files:**
- Modify: `xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj`

**Step 1: Add Cronos and CronExpressionDescriptor packages**

Run from repo root:
```bash
dotnet add xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj package Cronos
dotnet add xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj package CronExpressionDescriptor
```

**Step 2: Build to verify packages resolve**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj
git commit -m "chore: add Cronos and CronExpressionDescriptor NuGet packages"
```

---

### Task 2: Add CronHelper and cron computed properties

**Files:**
- Create: `xafhangfire/xafhangfire.Module/Helpers/CronHelper.cs`
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs`
- Test: `xafhangfire/xafhangfire.Jobs.Tests/CronHelperTests.cs`

**Step 1: Write failing tests for CronHelper**

```csharp
// xafhangfire/xafhangfire.Jobs.Tests/CronHelperTests.cs
using FluentAssertions;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Jobs.Tests;

public class CronHelperTests
{
    [Theory]
    [InlineData("*/5 * * * *", "Every 5 minutes")]
    [InlineData("0 9 * * 1-5", "At 09:00 AM, Monday through Friday")]
    [InlineData("0 0 1 * *", "At 12:00 AM, on day 1 of the month")]
    public void GetDescription_ValidCron_ReturnsHumanReadable(string cron, string expected)
    {
        var result = CronHelper.GetDescription(cron);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDescription_EmptyOrNull_ReturnsEmptyString(string? cron)
    {
        CronHelper.GetDescription(cron).Should().BeEmpty();
    }

    [Fact]
    public void GetDescription_InvalidCron_ReturnsErrorMessage()
    {
        var result = CronHelper.GetDescription("not-a-cron");
        result.Should().StartWith("Invalid:");
    }

    [Fact]
    public void GetNextRuns_ValidCron_ReturnsRequestedCount()
    {
        var result = CronHelper.GetNextRuns("*/5 * * * *", 5);
        result.Should().HaveCount(5);
    }

    [Fact]
    public void GetNextRuns_ValidCron_RunsAreInFuture()
    {
        var now = DateTime.UtcNow;
        var result = CronHelper.GetNextRuns("*/5 * * * *", 3);
        result.Should().AllSatisfy(dt => dt.Should().BeAfter(now));
    }

    [Fact]
    public void GetNextRuns_EmptyCron_ReturnsEmpty()
    {
        CronHelper.GetNextRuns("", 5).Should().BeEmpty();
    }

    [Fact]
    public void GetNextRuns_InvalidCron_ReturnsEmpty()
    {
        CronHelper.GetNextRuns("bad", 5).Should().BeEmpty();
    }

    [Fact]
    public void FormatNextRuns_ValidCron_ReturnsFormattedString()
    {
        var result = CronHelper.FormatNextRuns("*/5 * * * *", 3);
        result.Should().NotBeEmpty();
        result.Should().Contain("\n");
    }

    [Fact]
    public void FormatNextRuns_EmptyCron_ReturnsEmptyString()
    {
        CronHelper.FormatNextRuns("", 3).Should().BeEmpty();
    }
}
```

**Step 2: Run tests — verify they fail**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj --filter CronHelperTests
```
Expected: FAIL (CronHelper doesn't exist yet).

Note: The test project needs a reference to the Module project. Add it:
```bash
dotnet add xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj reference xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj
```

**Step 3: Implement CronHelper**

```csharp
// xafhangfire/xafhangfire.Module/Helpers/CronHelper.cs
using Cronos;
using CronExpressionDescriptor;

namespace xafhangfire.Module.Helpers
{
    public static class CronHelper
    {
        public static string GetDescription(string cronExpression)
        {
            if (string.IsNullOrWhiteSpace(cronExpression))
                return string.Empty;

            try
            {
                return ExpressionDescriptor.GetDescription(cronExpression);
            }
            catch
            {
                return $"Invalid: {cronExpression}";
            }
        }

        public static List<DateTime> GetNextRuns(string cronExpression, int count)
        {
            if (string.IsNullOrWhiteSpace(cronExpression) || count <= 0)
                return new List<DateTime>();

            try
            {
                var expression = CronExpression.Parse(cronExpression);
                var results = new List<DateTime>();
                var from = DateTime.UtcNow;

                for (var i = 0; i < count; i++)
                {
                    var next = expression.GetNextOccurrence(from, inclusive: false);
                    if (next == null) break;
                    results.Add(next.Value);
                    from = next.Value;
                }

                return results;
            }
            catch
            {
                return new List<DateTime>();
            }
        }

        public static string FormatNextRuns(string cronExpression, int count)
        {
            var runs = GetNextRuns(cronExpression, count);
            if (runs.Count == 0) return string.Empty;
            return string.Join("\n", runs.Select(r => r.ToLocalTime().ToString("yyyy-MM-dd HH:mm (ddd)")));
        }
    }
}
```

**Step 4: Run tests — verify they pass**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj --filter CronHelperTests
```
Expected: All pass.

**Step 5: Add computed properties to JobDefinition**

Add these properties to `JobDefinition.cs` after `CronExpression`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using xafhangfire.Module.Helpers;

// ... inside JobDefinition class, after CronExpression property:

[NotMapped]
[VisibleInListView(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public string CronDescription => CronHelper.GetDescription(CronExpression);

[NotMapped]
[FieldSize(FieldSizeAttribute.Unlimited)]
[VisibleInListView(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public string NextScheduledRuns => CronHelper.FormatNextRuns(CronExpression, 5);
```

**Step 6: Build and run all tests**

```bash
dotnet build xafhangfire.slnx
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```
Expected: Build succeeded. All tests pass.

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add cron expression visualization (description + next 5 runs)"
```

---

### Task 3: Create CommandMetadataProvider

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs`
- Create: `xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs`
- Test: `xafhangfire/xafhangfire.Jobs.Tests/CommandMetadataProviderTests.cs`

**Step 1: Write failing tests**

```csharp
// xafhangfire/xafhangfire.Jobs.Tests/CommandMetadataProviderTests.cs
using FluentAssertions;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Tests;

public class CommandMetadataProviderTests
{
    [Fact]
    public void GetMetadata_DemoLogCommand_ReturnsCorrectParameters()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(DemoLogCommand));

        metadata.Should().NotBeNull();
        metadata.Should().HaveCount(2);

        metadata![0].Name.Should().Be("Message");
        metadata[0].ParameterType.Should().Be(typeof(string));
        metadata[0].IsRequired.Should().BeTrue();
        metadata[0].DefaultValue.Should().BeNull();

        metadata[1].Name.Should().Be("DelaySeconds");
        metadata[1].ParameterType.Should().Be(typeof(int));
        metadata[1].IsRequired.Should().BeFalse();
        metadata[1].DefaultValue.Should().Be(3);
    }

    [Fact]
    public void GetMetadata_ListUsersCommand_ReturnsCorrectParameters()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(ListUsersCommand));

        metadata.Should().NotBeNull();
        metadata.Should().HaveCount(1);
        metadata![0].Name.Should().Be("MaxResults");
        metadata[0].DefaultValue.Should().Be(10);
    }

    [Fact]
    public void GetMetadata_GenerateReportCommand_IncludesComplexTypes()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(GenerateReportCommand));

        metadata.Should().NotBeNull();
        var reportParams = metadata!.FirstOrDefault(m => m.Name == "ReportParameters");
        reportParams.Should().NotBeNull();
        reportParams!.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void GetMetadata_UnknownType_ReturnsNull()
    {
        CommandMetadataProvider.GetMetadata("NonExistentCommand").Should().BeNull();
    }

    [Fact]
    public void GetRegisteredTypeNames_ReturnsAllCommandNames()
    {
        var names = CommandMetadataProvider.GetRegisteredTypeNames();

        names.Should().Contain(nameof(DemoLogCommand));
        names.Should().Contain(nameof(ListUsersCommand));
        names.Should().Contain(nameof(GenerateReportCommand));
        names.Should().Contain(nameof(SendEmailCommand));
        names.Should().Contain(nameof(SendReportEmailCommand));
        names.Should().Contain(nameof(SendMailMergeCommand));
    }

    [Fact]
    public void GetMetadata_SendEmailCommand_HasRequiredFields()
    {
        var metadata = CommandMetadataProvider.GetMetadata(nameof(SendEmailCommand));

        metadata.Should().NotBeNull();
        var toParam = metadata!.First(m => m.Name == "To");
        toParam.IsRequired.Should().BeTrue();
        toParam.ParameterType.Should().Be(typeof(string));
    }
}
```

**Step 2: Run tests — verify they fail**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj --filter CommandMetadataProviderTests
```

**Step 3: Create CommandParameterMetadata**

```csharp
// xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs
#nullable enable

namespace xafhangfire.Jobs;

public sealed record CommandParameterMetadata(
    string Name,
    Type ParameterType,
    bool IsRequired,
    object? DefaultValue);
```

**Step 4: Create CommandMetadataProvider**

```csharp
// xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs
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

    public static IReadOnlyList<CommandParameterMetadata>? GetMetadata(string jobTypeName)
    {
        if (!CommandTypes.TryGetValue(jobTypeName, out var commandType))
            return null;

        var ctor = commandType.GetConstructors().FirstOrDefault();
        if (ctor == null) return null;

        return ctor.GetParameters().Select(p => new CommandParameterMetadata(
            Name: p.Name!,
            ParameterType: p.ParameterType,
            IsRequired: !p.HasDefaultValue,
            DefaultValue: p.HasDefaultValue ? p.DefaultValue : null
        )).ToList();
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

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add CommandMetadataProvider with reflection-based parameter discovery"
```

---

### Task 4: Create JobParametersPropertyEditor (Blazor)

This task creates the custom XAF Blazor property editor that replaces the raw JSON textarea with typed form fields.

**Files:**
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs`
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor`
- Create: `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs`
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs` (change EditorAlias)

**Step 1: Create the component model**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs
using DevExpress.ExpressApp.Blazor.Components.Models;

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

    public Action<string>? OnJsonChanged { get; set; }

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
```

**Step 2: Create the Blazor component**

```razor
@* xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor *@
@using DevExpress.Blazor
@using DevExpress.ExpressApp.Blazor.Components

<ComponentModelObserver ComponentModel="@ComponentModel">
    <Content>
        @if (ComponentModel.ShowRawEditor)
        {
            <DxMemo Value="@ComponentModel.RawJson"
                    ValueChanged="@((string v) => OnRawJsonChanged(v))"
                    Rows="6" />
        }
        else
        {
            @foreach (var field in ComponentModel.Fields)
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
    </Content>
</ComponentModelObserver>

@code {
    [CascadingParameter]
    public JobParametersFormModel ComponentModel { get; set; } = null!;

    private void OnRawJsonChanged(string value)
    {
        ComponentModel.RawJson = value;
        ComponentModel.OnJsonChanged?.Invoke(value);
    }

    private void OnStringChanged(ParameterFieldModel field, string value)
    {
        field.StringValue = value;
        NotifyJsonChanged();
    }

    private void OnIntChanged(ParameterFieldModel field, int value)
    {
        field.IntValue = value;
        NotifyJsonChanged();
    }

    private void OnBoolChanged(ParameterFieldModel field, bool value)
    {
        field.BoolValue = value;
        NotifyJsonChanged();
    }

    private void NotifyJsonChanged()
    {
        var json = SerializeFields();
        ComponentModel.RawJson = json;
        ComponentModel.OnJsonChanged?.Invoke(json);
    }

    private string SerializeFields()
    {
        var dict = new Dictionary<string, object?>();
        foreach (var field in ComponentModel.Fields)
        {
            switch (field.FieldType)
            {
                case "int":
                    dict[field.Name] = field.IntValue;
                    break;
                case "bool":
                    dict[field.Name] = field.BoolValue;
                    break;
                case "json":
                    // Try to parse as JSON, fall back to string
                    try
                    {
                        dict[field.Name] = System.Text.Json.JsonSerializer.Deserialize<object>(field.StringValue);
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
        return System.Text.Json.JsonSerializer.Serialize(dict,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}
```

**Step 3: Create the property editor**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs
using System.Text.Json;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
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
        model.OnJsonChanged = json =>
        {
            model.RawJson = json;
            OnControlValueChanged();
            WriteValue();
        };
        return model;
    }

    protected override void ReadValueCore()
    {
        base.ReadValueCore();
        var json = (string)PropertyValue ?? string.Empty;
        ComponentModel.RawJson = json;
        RefreshFields(json);
    }

    protected override object GetControlValueCore() => ComponentModel.RawJson;

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

    private string? GetJobTypeName()
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
        // "DelaySeconds" → "Delay Seconds"
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

**Step 4: Change EditorAlias on ParametersJson**

In `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs`, change the `ParametersJson` property:

Replace:
```csharp
[FieldSize(FieldSizeAttribute.Unlimited)]
[EditorAlias("StringPropertyEditor")]
public virtual string ParametersJson { get; set; }
```

With:
```csharp
[FieldSize(FieldSizeAttribute.Unlimited)]
[EditorAlias("JobParametersEditor")]
public virtual string ParametersJson { get; set; }
```

**Step 5: Build**

```bash
dotnet build xafhangfire.slnx
```
Expected: Build succeeded.

**Step 6: Run all tests**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```
Expected: All pass.

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add rich parameter editor with typed form fields for JobDefinition"
```

---

### Task 5: Update docs and final verification

**Files:**
- Modify: `TODO.md`
- Modify: `README.md`

**Step 1: Build entire solution**

```bash
dotnet build xafhangfire.slnx
```

**Step 2: Run all tests**

```bash
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```

**Step 3: Update TODO.md**

Add session status and completed items for cron visualization and parameter UI.

**Step 4: Update README.md**

Add cron visualization and parameter UI to key features list.

**Step 5: Commit**

```bash
git add -A
git commit -m "docs: update TODO and README with cron visualization and parameter UI"
```
