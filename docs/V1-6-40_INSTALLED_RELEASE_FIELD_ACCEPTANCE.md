# ARSAS v1.6.40 — installed stable release field acceptance

**Status:** Maintainer-accepted stable release; installed-package physical diagnostic recorded on 23 September 2026 (UTC+07:00).

This record closes the documentation gap between the [physically rejected v1.6.39](../evidence/v1.6.39-physical-rejection.json) and the successfully released v1.6.40 Smart Discovery production route. The maintainer confirms that v1.6.40 was released after successful Smart Discovery physical acceptance. The attached operator diagnostic, summarized here without publishing the raw file, independently identifies the installed v1.6.40 application and pinned engine and shows successful live discovery and static reporting in the recorded session.

## Exact installed-package identity

| Item | Field-observed identity |
| --- | --- |
| ARSAS stable tag | [v1.6.40](https://github.com/masarray/arsas/releases/tag/v1.6.40) |
| Application commit | `1b26dc1235b3bac0300bf84c1b5e8fc590934bc5` |
| ARIEC61850 commit | `648124097621046f5f127ceb1cf853fea54db730` |
| Test device | `AA1E1F06R4` (existing documented physical reference) |
| Evidence kind | Installed release diagnostic; not a simulation or CI-only fixture |
| Diagnostic timestamp | 2026-09-23 20:36:50 UTC+07:00 |

The application and engine commits match [published release provenance](../.release/published.json). The diagnostic identifies the installed package, but does **not** include an independent executable-file SHA-256 or prove that the portable executable was physically exercised. The published release checksum and provenance are separate evidence.

## Observed physical session

| Measurement | Recorded result |
| --- | ---: |
| Smart Discovery time | 2,395.3 ms |
| Smart Discovery KPI confirmed requests | 204 |
| Duplicate requests | 0 |
| Peak outstanding / negotiated calling | 8 / 10 |
| Logical Devices / Logical Nodes | 32 / 119 |
| Data Objects / Data Attributes | 895 / 6,335 |
| LN-root type probes | 119/119 successful |
| DataSets / directory reads | 2 / 2 of 2 |
| Static DataSet members represented | 58/58 |
| Analog / Digital | 22/22 / 36/36 |
| Configured static RCB plans | 2 (Analog URCB and Digital BRCB) |
| Selected / report-covered runtime points | 58 / 58 |
| Final unavailable or unresolved runtime points | 0 |
| Cyclic MMS process polling | 0 |

The session used the tracked `SMART-CAPTURE PR134 P0-5c` route, one association-scoped discovery owner, exclusive application MMS gate and deferred initial FC-root reads. The Analog `RP.Unbuffer01` (22 members) and Digital `BR.Buffer01` (36 members) subscriptions were reported active. Subsequent `REPORT_SEMANTIC_STRUCT` entries record structured report updates after monitoring started. No cyclic MMS process-value polling or dynamic DataSet creation was used in this Static DataSet mode.

## Interpret diagnostics correctly

- `Primary unresolved=14` refers to inventory descriptors before exact structured/static projection; it is **not** the final monitor result. The selected 58 runtime rows were reported covered and final unresolved count was **0**.
- The two RCB planning entries reported availability `Unknown` because availability metadata was incomplete; the log subsequently reports actual successful RCB activation. Do not classify this as a failed subscription.
- A Digital BRCB explicit `ResvTms` write was rejected as `type-inconsistent`; the client continued via the implicit reservation/`RptEna=true` path, and the BRCB was reported active. Preserve the warning as an observed interoperability detail rather than claiming every setup write succeeded.
- `REPORT_SEMANTIC_STRUCT` warnings describe schema-driven structured-value expansion, not `REPORT_VALUE_REJECTED`.
- This snapshot supports the stated successful installed-release session; it does not measure multi-hour stability, prove a fresh raw-PCAP service count, or assert universal compatibility with other IEDs.

## Release and regression boundary

The [v1.6.39 rejection](../evidence/v1.6.39-physical-rejection.json) documented wrong production compilation, ~31.622 s discovery, and only 23/58 report-backed points. v1.6.40 corrected the production build route via [PR #347](https://github.com/masarray/arsas/pull/347) and was published from the exact source and engine shown above. This installed-package diagnostic confirms the successful Smart Discovery/report-only runtime shape and must **not** be reinterpreted as a pending requalification of the already released baseline.

Do not change the production Smart Discovery route, pinned engine, Static DataSet acquisition authority, RCB activation or report projection as a side effect of evidence/documentation cleanup. Future changes in those paths require their own bounded regression and field acceptance.

Machine-readable sanitized record: [`evidence/v1.6.40-installed-release-field-verification.json`](../evidence/v1.6.40-installed-release-field-verification.json). The operator's raw diagnostic is intentionally not committed: it includes private network topology, a machine identifier, local paths and MMS hex excerpts.
