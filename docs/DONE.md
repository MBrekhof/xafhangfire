# DONE

## 2026-10-26
- [x] Authentication API likely blocked by default JWT policy. Refuted by repro: a bad-credentials POST returns XAF's 401. (ID: 2553)
- [x] ReportController `[ValidateAntiForgeryToken]` may reject JWT Bearer GETs. Refuted by repro: a Bearer-only GET returns 200 and a PDF. (ID: 2569)
- [x] HangfireJob service user has an empty password; not seeded at all in Release builds. Fixed in `971427f`. (ID: 2568)
- [x] Hangfire recurring job IDs collide per command type. Fixed in `737794f` (one schedule per job definition). (ID: 2554)
- [x] Execution records don't link to JobDefinition. Fixed in `737794f`. (ID: 2555)
- [x] Disabled/deleted JobDefinitions keep their Hangfire recurring jobs. Fixed in `737794f`. (ID: 2556)
- [x] RunNow overwrites the finished status with Running in Direct mode. Fixed in `737794f`. (ID: 2570)
- [x] A previous failure's message stays visible while the job runs again. Fixed in `737794f`. (ID: 2575)
- [x] Report export/email output path collisions under concurrency. Fixed in `239809a`. (ID: 2557)
- [x] Parameter editor must not rewrite ParametersJson on load (umbrella). Fixed in `a722a31`. (ID: 2567)
- [x] Blank json job parameter serializes as "" and breaks command deserialization. Fixed in `a722a31`. (ID: 2564)
- [x] Parameter editor wipes relative date terms on load. Fixed in `a722a31`. (ID: 2565)
- [x] Empty Organization lookup breaks the Contact List report filter. Fixed in `a722a31`. (ID: 2566)
- [x] Clearing ReportName turns the old report's parameters into key/value rows. Fixed in `a722a31`. (ID: 2573)
- [x] Decimal report parameters are parsed with the server culture. Fixed in `a722a31`. (ID: 2574)
- [x] Batch-1 tests that passed against the old code. Fixed in `a722a31`. (ID: 2576)
