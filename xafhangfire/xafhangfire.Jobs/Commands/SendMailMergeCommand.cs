namespace xafhangfire.Jobs.Commands;

public record SendMailMergeCommand(
    string TemplateName,
    string? OrganizationFilter = null);
