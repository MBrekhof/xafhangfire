# Test Project Design

**Date:** 2026-02-21
**Goal:** Add xUnit test project covering the Jobs class library with pure unit tests.

## Scope

**In scope:** `xafhangfire.Jobs` project — pure logic, no XAF/DB dependencies.

**Out of scope:** Blazor.Server handlers (XAF dependencies), XAF entities, Hangfire integration tests.

## Tech Stack

- xUnit
- FluentAssertions
- NSubstitute (mocking)

## Test Coverage

| Class | Tests |
|-------|-------|
| DateRangeResolver | All 12 terms, unknown term error, relative date handling |
| JobDispatchService | All 6 command types, unknown type throws, null params use defaults |
| DirectJobDispatcher | Creates scope, calls initializer, calls handler, records execution |
| ReportParameterHelper | Skipped (lives in Blazor.Server, not Jobs) |
| SendEmailHandler | Delegates to IEmailSender with correct args |
| DemoLogHandler | Completes with message, respects cancellation |
| LogOnlyEmailSender | Completes without throwing |

## Project Structure

```
xafhangfire/xafhangfire.Jobs.Tests/
  xafhangfire.Jobs.Tests.csproj
  DateRangeResolverTests.cs
  JobDispatchServiceTests.cs
  DirectJobDispatcherTests.cs
  Handlers/
    SendEmailHandlerTests.cs
    DemoLogHandlerTests.cs
  LogOnlyEmailSenderTests.cs
```
