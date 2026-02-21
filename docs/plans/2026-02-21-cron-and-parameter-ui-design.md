# Cron Visualization & Rich Parameter UI Design

**Date:** 2026-02-21
**Goal:** Add human-readable cron descriptions with next-run previews, and replace raw JSON parameter editing with dynamic typed forms.

## Feature 1: Cron Expression Visualization

**Approach:** Two non-persistent computed properties on `JobDefinition`:
- `CronDescription` (string) — e.g., "Every 5 minutes"
- `NextScheduledRuns` (string) — formatted list of next 5 run times

Computed server-side using **Cronos** NuGet package (MIT, zero dependencies). Read-only fields in detail view. No database changes.

**NuGet:** `Cronos` added to `xafhangfire.Module`.

**XAF integration:** `[NotMapped]` properties with getters that parse `CronExpression` via Cronos. Displayed in detail view, hidden in list view.

## Feature 2: Rich Parameter UI

**Approach:** Reflect on command record types to discover constructor parameters. Generate typed form fields instead of raw JSON textarea.

### Components

- **`CommandParameterMetadata`** — record in Jobs project: `(string Name, Type Type, bool Required, object? DefaultValue)`
- **`CommandMetadataProvider`** — static helper in Jobs project. Reflects on command type to extract parameter list. Maps `JobTypeName` string → `Type` using a registry dictionary.
- **`JobParameterEditorController`** — XAF ViewController in Module project. On detail view load or `JobTypeName` change:
  1. Gets metadata via `CommandMetadataProvider.GetMetadata(jobTypeName)`
  2. Deserializes current `ParametersJson` into values
  3. Creates dynamic property editors for each parameter
  4. On save, serializes back to `ParametersJson`

### Field Type Mapping

| C# Type | XAF Editor |
|---------|------------|
| `string` | StringPropertyEditor |
| `int` | IntegerPropertyEditor |
| `bool` | BooleanPropertyEditor |
| `string?` (nullable) | StringPropertyEditor (not required) |
| `Dictionary<string, string>?` | Raw JSON textarea (fallback) |
| `IReadOnlyList<string>?` | Raw JSON textarea (fallback) |

### Behavior

- Raw `ParametersJson` textarea hidden when metadata is available
- Falls back to raw JSON if command type unknown or reflection fails
- `JobTypeName` change refreshes the parameter form
