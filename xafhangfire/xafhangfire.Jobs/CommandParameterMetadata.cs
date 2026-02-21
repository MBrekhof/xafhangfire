#nullable enable

namespace xafhangfire.Jobs;

public sealed record CommandParameterMetadata(
    string Name,
    Type ParameterType,
    bool IsRequired,
    object? DefaultValue);
