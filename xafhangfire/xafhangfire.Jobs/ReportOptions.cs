namespace xafhangfire.Jobs;

public record ReportOutputOptions
{
    public string OutputDirectory { get; init; } = "reports";
}
