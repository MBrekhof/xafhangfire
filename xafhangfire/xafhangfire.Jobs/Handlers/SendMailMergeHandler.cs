using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class SendMailMergeHandler(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    IEmailSender emailSender,
    ILogger<SendMailMergeHandler> logger) : IJobHandler<SendMailMergeCommand>
{
    public async Task ExecuteAsync(
        SendMailMergeCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting mail merge with template '{Template}', filter: {Filter}",
            command.TemplateName, command.OrganizationFilter ?? "(all)");

        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace(typeof(object));

        // Load template — use dynamic to avoid Module dependency
        var templateType = objectSpace.TypesInfo.FindTypeInfo("xafhangfire.Module.BusinessObjects.EmailTemplate")?.Type;
        if (templateType == null)
            throw new InvalidOperationException("EmailTemplate type not found");

        var templates = objectSpace.GetObjects(templateType);
        object? template = null;
        foreach (var t in templates)
        {
            var nameProp = templateType.GetProperty("Name");
            if (nameProp?.GetValue(t)?.ToString() == command.TemplateName)
            {
                template = t;
                break;
            }
        }

        if (template == null)
            throw new InvalidOperationException($"Email template '{command.TemplateName}' not found");

        var subjectTemplate = templateType.GetProperty("Subject")?.GetValue(template)?.ToString() ?? "";
        var bodyTemplate = templateType.GetProperty("BodyHtml")?.GetValue(template)?.ToString() ?? "";

        // Load contacts
        var contactType = objectSpace.TypesInfo.FindTypeInfo("xafhangfire.Module.BusinessObjects.Contact")?.Type;
        if (contactType == null)
            throw new InvalidOperationException("Contact type not found");

        var contacts = objectSpace.GetObjects(contactType);

        int sent = 0, failed = 0;

        foreach (var contact in contacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstName = contactType.GetProperty("FirstName")?.GetValue(contact)?.ToString() ?? "";
            var lastName = contactType.GetProperty("LastName")?.GetValue(contact)?.ToString() ?? "";
            var fullName = contactType.GetProperty("FullName")?.GetValue(contact)?.ToString() ?? "";
            var email = contactType.GetProperty("Email")?.GetValue(contact)?.ToString() ?? "";
            var jobTitle = contactType.GetProperty("JobTitle")?.GetValue(contact)?.ToString() ?? "";

            var orgProp = contactType.GetProperty("Organization");
            var org = orgProp?.GetValue(contact);
            var orgName = org?.GetType().GetProperty("Name")?.GetValue(org)?.ToString() ?? "";

            // Apply organization filter
            if (!string.IsNullOrEmpty(command.OrganizationFilter) &&
                !orgName.Equals(command.OrganizationFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogWarning("Skipping contact '{Name}' — no email address", fullName);
                continue;
            }

            var subject = ReplacePlaceholders(subjectTemplate, firstName, lastName, fullName, email, jobTitle, orgName);
            var body = ReplacePlaceholders(bodyTemplate, firstName, lastName, fullName, email, jobTitle, orgName);

            try
            {
                await emailSender.SendAsync(email, subject, body, isHtml: true, cancellationToken: cancellationToken);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send mail merge email to '{Email}'", email);
                failed++;
            }
        }

        logger.LogInformation("Mail merge complete — Sent: {Sent}, Failed: {Failed}", sent, failed);
    }

    private static string ReplacePlaceholders(
        string template,
        string firstName, string lastName, string fullName,
        string email, string jobTitle, string orgName)
    {
        return template
            .Replace("{FirstName}", firstName)
            .Replace("{LastName}", lastName)
            .Replace("{FullName}", fullName)
            .Replace("{Email}", email)
            .Replace("{JobTitle}", jobTitle)
            .Replace("{Organization.Name}", orgName);
    }
}
