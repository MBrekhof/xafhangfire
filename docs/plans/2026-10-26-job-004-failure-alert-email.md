# JOB-004: Email alert on repeated job failures

Card: JOB-004 (CARD-2562). Today `XafJobExecutionRecorder.RecordFailureAsync` increments
`JobDefinition.ConsecutiveFailures`, and at `>= Jobs:FailureAlertThreshold` (default 3) it only logs a
warning. Goal: also send an email via `IEmailSender`.

## Design (after Gemini's plan review)

- **Config:** add `Jobs:FailureAlertRecipients` to appsettings.json, a list separated by `;` or `,`.
  Empty or missing means no email. No env vars.
- **No new class.** `XafJobExecutionRecorder` gets `IEmailSender` (transient, both the SMTP and the
  log-only sender) and an `internal Task AlertOnRepeatedFailuresAsync(string jobName, int
  consecutiveFailures, string errorMessage)`, which tests call directly (InternalsVisibleTo exists).
  - The existing warning log moves here and is guarded: `threshold > 0 && count >= threshold`.
    It used to fire on every failure when the threshold was 0 or less.
  - The email goes out only when `threshold > 0 && count == threshold`: one alert per failure
    streak. A success resets the counter, so the next streak alerts again. Failures past the
    threshold don't resend, so a broken every-minute job doesn't send an email a minute.
  - One `SendAsync` per recipient (`SmtpEmailSender` takes a single address). Each send has its own
    try/catch, so one bad address doesn't stop the rest.
  - Plain text (`isHtml: false`). The job name has its line breaks replaced in the subject.
  - **Only the error's first line** (`ExceptionType: message`, at most 500 chars) goes in the email.
    The caller passes `ex.ToString()`, and a stack trace doesn't belong in an inbox. The body points
    to the execution record for the full error.
  - Uses `CancellationToken.None`: the failure may be a cancellation, and the alert must still go out.
  - The whole method is wrapped in a try/catch, so nothing (e.g. a malformed threshold setting) can
    escape and replace the job's own exception in the caller.
- **Recorder failure path:** the ObjectSpace read-increment-commit runs under a static lock (also
  around the completion path's reset). Otherwise two concurrent failing runs of one definition
  (recurring run plus retry, or Run Now twice) could both see `== threshold` and send twice, or
  lose an increment. `ponytail:` process-wide lock: one app instance runs the Hangfire server; an
  atomic SQL `UPDATE … SET ConsecutiveFailures = ConsecutiveFailures + 1 RETURNING` would be the
  upgrade if it scales out.
  The alert values are captured after `CommitChanges()`. The alert runs after the ObjectSpace is
  disposed, so no DB connection is held during SMTP. A failed commit sends no alert.
  Ad-hoc runs without a `JobDefinition` have no counter and never alert (unchanged).

## Cases (self-review)

| Case | Covered by |
|---|---|
| count < threshold → no email | unit test |
| count == threshold → one email per recipient, plain text, subject/body contain name, count and the error's first line | unit test |
| count > threshold → no email | unit test |
| recipients null (missing key) / empty / whitespace / `" ; "` → no email | unit test (theory) |
| recipients separated by `,` and `;` | unit test |
| threshold 0 / negative → no email | unit test |
| sender throws for recipient 1 → recipient 2 still gets it; no exception escapes | unit test |
| malformed threshold setting → no exception escapes | unit test |
| stack trace not in the email; first line truncated to 500 | unit test |
| job name with line breaks → single-line subject | unit test |
| concurrent failures of one definition | static lock (code); not unit-tested (needs a real ObjectSpace) |
| end-to-end: failing definition with LogOnlyEmailSender → alert logged once at the 3rd failure | live check with an every-minute GenerateReport for an unknown report |

## Gemini plan review (2026-10-26): all adopted

1. Race on `ConsecutiveFailures++` → static lock.
2. Threshold ≤ 0 made the warning fire on every failure; missing recipients key → explicit guards.
3. ObjectSpace held across SMTP; an alert exception could replace the job exception → alert after
   dispose, everything in try/catch.
4. `ex.ToString()` would email stack traces → first line only.
5. Separate alerter class didn't earn its keep → internal method on the recorder.
6. Comma-separated recipients would pass with the log-only sender and fail with SMTP → split on both;
   line breaks in the job name → replaced.

## Gemini diff review (2026-10-26): nothing blocking

1. `[AutomaticRetry(Attempts = 3)]`: every attempt counts as a failure, so one run can reach the threshold before
   its retries are done. Reproduced live (below). Not changed here: it's a decision for Martin (JOB-009).
2. Wrapped exceptions: the first line was only the wrapper ("See the inner exception for details."). Reproduced
   with a failing test, then fixed: the summary keeps the ` ---> Inner: message` lines up to the first stack frame.
3. Tests that a wrong implementation would pass: added threshold -1 at equality, a custom threshold of 2, and
   warning-count assertions (no warning when alerts are off; a warning past the threshold).
4. The failing-recipient test stubbed every recipient to throw → stub only the bad one, assert both were tried.
5. Test error text used `\n` → `\r\n`, as a real `ex.ToString()` on Windows; asserts no `\r` in the body.
6. Indentation inside the completion lock.

## Live check (2026-10-26)

Every-minute GenerateReport definition for an unknown report, two recipients, LogOnlyEmailSender. Failure 3
(20:51:05 UTC) logged one alert to each recipient: plain text, first line of the
`UserFriendlyException`, no stack trace. Failures 4 and 5 warned without sending. The count mixed attempts:
failure 3 was the first attempt of the next firing, while the previous firing still had retry 3 of 3 pending.

## Open questions for Martin

- Re-alerting while a job keeps failing (e.g. every N failures) is not built. The default is once per streak.
- Whether retries count toward the threshold (JOB-009).
