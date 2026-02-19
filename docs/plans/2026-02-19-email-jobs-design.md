# Email Jobs Design

**Date:** 2026-02-19
**Status:** Approved

## Overview

Add email capabilities to the job dispatcher: notifications, report delivery as attachments, and mail merge from CRM contacts with database-stored templates.

## Commands

| Command | Purpose |
|---------|---------|
| `SendEmailCommand` | Single email (to, subject, body, optional attachments) |
| `SendReportEmailCommand` | Generate report + email as attachment |
| `SendMailMergeCommand` | Bulk personalized emails from template + CRM contacts |

## Architecture

- **MailKit** for SMTP delivery (added to Blazor.Server only)
- **IEmailSender** interface in Jobs project (no MailKit dependency)
- **SmtpEmailSender** in Blazor.Server — real MailKit SMTP
- **LogOnlyEmailSender** in Jobs — dev fallback when no SMTP configured
- Config toggle: `Email:Smtp:Host` present → SMTP, absent → log-only

## EmailTemplate Entity

| Property | Type | Notes |
|----------|------|-------|
| Name | string | Unique identifier |
| Subject | string | With `{Placeholder}` syntax |
| BodyHtml | string (unlimited) | HTML with `{Placeholder}` syntax |
| Description | string | Admin notes |

Placeholders: `{FirstName}`, `{LastName}`, `{FullName}`, `{Email}`, `{JobTitle}`, `{Organization.Name}` — resolved via simple `string.Replace`.

## Data Flow

**SendEmail:** Command → Handler → IEmailSender.SendAsync()

**SendReportEmail:** Command → Handler → IReportExportService (export to temp) → IEmailSender.SendAsync(with attachment) → cleanup

**SendMailMerge:** Command → Handler → Load template from ObjectSpace → Query contacts → Replace placeholders per contact → IEmailSender.SendAsync() per contact

## Error Handling

- SMTP failure: let Hangfire retry
- Mail merge partial failure: log per-contact, continue, report summary
- Missing template: throw InvalidOperationException
- Empty contact list: log warning, no-op

## Config

```json
"Email": {
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "Username": "user",
    "Password": "pass",
    "UseSsl": true,
    "FromAddress": "noreply@example.com",
    "FromName": "XAF Hangfire App"
  }
}
```

## File Locations

- Commands: `Jobs/Commands/`
- IEmailSender + LogOnlyEmailSender: `Jobs/`
- SmtpEmailSender: `Blazor.Server/Services/`
- SendEmailHandler + SendMailMergeHandler: `Jobs/Handlers/`
- SendReportEmailHandler: `Blazor.Server/Handlers/` (needs IReportExportService)
- EmailTemplate: `Module/BusinessObjects/`
