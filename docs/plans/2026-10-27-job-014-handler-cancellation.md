# JOB-014: job handlers and the job's CancellationToken

Card: JOB-014 (CARD-2591). Was unverified; checked 2026-10-27.

## What the check found

| Card claim | Verdict | Evidence |
|---|---|---|
| SendMailMergeHandler's per-recipient `catch (Exception)` swallows the cancellation, so a cancelled run is recorded as Success | **Reproduced** | `HandlerCancellationTests.MailMerge_CancelledDuringLastSend_Throws` fails: no exception |
| GenerateReportHandler creates the output file before `CopyToAsync` checks the token, leaving a 0-byte file | **Reproduced** | `GenerateReport_CancelledDuringExport_ThrowsAndLeavesNoFile` fails: a 0-byte `.pdf` is left |
| GenerateReport/SendReportEmail call `ExportReportAsync` without the token | **True, DevExpress-inherent** | `IReportExportService.ExportReportAsync(XtraReport, ExportTarget)` has no token parameter (26.1 source, `IReportExportService.cs:56`). A stop during a long export waits for DX; nothing to pass. By design. |
| SendReportEmailHandler leaves files behind | **Not a bug** | Its per-run temp folder is deleted in `finally`; the OperationCanceledException propagates |
| ListUsersHandler ignores the token | **True, harmless** | One short query and log lines; there is no long work to cancel. No change. |

## Changes

1. `Jobs/Handlers/SendMailMergeHandler.cs`, the per-recipient catch:
   ```csharp
   catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
   ```
   A cancellation of the job propagates; any other failure, including a send timeout that surfaces as a
   TaskCanceledException while the job's token is not cancelled, still counts as one failed recipient and the
   loop continues.

2. `Blazor.Server/Handlers/GenerateReportHandler.cs`, after the export:
   ```csharp
   await using var stream = await reportExportService.ExportReportAsync(report, exportTarget);
   // JOB-014: DX's export takes no token; check it before touching the output file.
   cancellationToken.ThrowIfCancellationRequested();
   try
   {
       await using (var fileStream = File.Create(outputPath))
           await stream.CopyToAsync(fileStream, cancellationToken);
   }
   catch
   {
       File.Delete(outputPath); // no file unless it was written completely
       throw;
   }
   ```
   The check before `File.Create` also keeps a pre-existing file at an explicit `OutputPath` intact when the run
   is cancelled during the export. The catch removes a partial file on a cancellation mid-copy and on any other
   write failure (disk full), since a truncated report is never useful. `File.Delete` on a missing file is a no-op.

## Cases and checks

| Case | Expected | Check |
|---|---|---|
| Mail merge cancelled during the last send | Throws OperationCanceledException | `MailMerge_CancelledDuringLastSend_Throws` (fails now) |
| Mail merge cancelled between contacts | Throws (existing `ThrowIfCancellationRequested` at the loop top) | Code read, unchanged |
| A send fails with an ordinary exception | Counted as failed, next contact sent | `MailMerge_SendFailure_CountsAsFailedRecipientAndContinues` (passes now; guards the fix) |
| A send times out (TaskCanceledException), job not cancelled | Counted as failed, next contact sent | `MailMerge_TimeoutWhileJobNotCancelled_CountsAsFailedRecipientAndContinues` (passes now; guards the fix) |
| Report cancelled during the DX export | Throws, no file written | `GenerateReport_CancelledDuringExport_ThrowsAndLeavesNoFile` (fails now) |
| Report cancelled mid-copy / write failure | Partial file deleted, exception propagates | Same catch; covered by code read |
| Normal report and report-email runs | Unchanged | Existing `ReportHandlerOutputTests` |
| How the run is recorded | OperationCanceledException with the token cancelled → interrupted (JOB-011 logic in JobExecutor / DirectJobDispatcher) | Existing `JobCancellationTests` |

## Gemini plan review (2026-10-27): diagnosis confirmed, two should-fix, two nits

1. Adopted (should-fix): `File.Create` inside the try made `File.Delete` run when the create itself failed, and an
   unguarded delete could replace the original exception (turning a cancellation into a recorded failure, see
   `JobCancellationTests.OtherException_WhileCancelled_IsAFailure`). Now: create before the try; delete is best
   effort with a logged warning, as in SendReportEmailHandler.
2. Adopted (should-fix): the first test never reached the catch. Added `GenerateReport_CancelledMidCopy_DeletesPartialFile`
   (a stream that cancels after the first read) and `GenerateReport_CancelledDuringExport_LeavesExistingExplicitFileUntouched`.
3. Adopted (nit): MailKit wraps a cancelled TLS handshake in `SslHandshakeException` and swallows a cancellation during
   QUIT, so a cancelled last send could still end as a success. One generic check after the loop
   (`cancellationToken.ThrowIfCancellationRequested()`) closes every such case; test
   `MailMerge_CancellationWrappedBySender_StillThrows`. The exception filter stays for the log: without it a cancelled
   send is logged as a failed recipient on every graceful stop.
4. Open question for Martin: `XtraReport.ExportToPdfAsync/ExportToXlsxAsync` do take a token;
   `IReportExportService.ExportReportAsync` calls them with `default`. Calling them directly would make a long export
   stop on shutdown, at the cost of bypassing the service seam the handler tests mock.

## Results

- 7 tests in `HandlerCancellationTests`; 188/188 in the suite.
- Each guard removed in turn fails its test: token check before `File.Create` →
  `LeavesExistingExplicitFileUntouched`; partial-file delete → `CancelledMidCopy_DeletesPartialFile`; post-loop
  check → `CancellationWrappedBySender_StillThrows`; filter and post-loop check both removed →
  `CancelledDuringLastSend_Throws` as well.

No live run: both fixes are inside handler code that the unit tests drive end to end, and JOB-011 already covers how
the executor records a cancellation.
