# DONE

## 2026-10-26
- [x] Authentication API likely blocked by default JWT policy. Refuted by repro: a bad-credentials POST returns XAF's 401. (ID: 2553)
- [x] ReportController `[ValidateAntiForgeryToken]` may reject JWT Bearer GETs. Refuted by repro: a Bearer-only GET returns 200 and a PDF. (ID: 2569)
