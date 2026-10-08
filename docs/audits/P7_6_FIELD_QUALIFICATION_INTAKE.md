# P7.6 — Read-only dual-ingress field diagnostic intake (draft)

Parent: issue #446, stacked after draft P7.5 PR #476. No Studio runtime, MMS engine, IED association, RCB planner, native installer, physical R10 or release authority changes.

## Purpose

P7.3 records semantic Discovery/Open-SCL fingerprints; P7.4 separately tracks actual routed values on each exact static BRCB and URCB target. The native Studio Copy Diagnostic truncates individual RCB target lines to eight per ingress. Semantic MATCH alone is not traffic proof.

This milestone adds a fail-closed local validator of one **full native Studio Copy Diagnostic** for exactly one IED. It requires a matching SHA-256 fingerprint in both independent ingress summaries, explicit dual-ingress traffic qualification, nonzero selected static BRCB and URCB coverage, zero uncovered signals, an unambiguous complete per-ingress target inventory, accepted ROUTED status per distinct exact RCB, and a timezone-aware first routed timestamp. It rejects missing/duplicate IED sections, emergency reports, mismatched ingresses, empty or ambiguous target identities, any target inventory truncated beyond eight and wrong selected AccessPoint.

Its JSON output is explicitly DIAGNOSTIC_CONSISTENCY_PASS_NOT_PHYSICAL_VERIFIED. Both physicalVerification and wirePcapVerification are **false** regardless of an otherwise valid diagnostic. Offline text can be copied or forged; this validator is not independent wire or physical verification.

## Local 7SX85 field procedure

1. Use the live IED Discovery IP path and choose a Static DataSet report-only selection covering BRCB and URCB. Wait for genuine routed process values on all configured targets, not only RptEna/GI.
2. Open the same SCD, choose the exact MMS AccessPoint J or F from the existing native WPF control, reconnect using a fresh MMS association, and repeat. Do not use cyclic MMS process polling as a reporting fallback.
3. Save the **full Copy Diagnostic** only after both ingresses have run in that device session. Keep customer names, endpoints, addresses and the original unredacted report outside the repository.
4. Run locally with the actual IED and case-specific AP and thresholds:

    python scripts/verify-p7-dual-ingress-diagnostic.py --diagnostic ./private-diagnostic.txt --ied GR_X_7SX85 --selected-ap F --min-brcb-signals 6 --min-urcb-signals 6 --output ./private-result.json

5. Independently review **real MMS/PCAP** for both APs and both planned report families; inspect association/RCB/DataSet, accepted InformationReports, timestamps and absence of cyclic MMS process polling. Securely record source/engine SHA and PCAP provenance. The offline tool cannot assert physical identity, packet origin, or actual AP J/F connectivity.

## Anti-naive acceptance limitations

- The source-native format must include exactly one explicit MATCH and DUAL INGRESS TRAFFIC PROVEN for the exact IED, plus two different ingress identities. It does not rely on matching indexed RCB instance names across different associations.
- The tool enumerates all reported concrete targets and demands equality with the planning count. Because Copy Diagnostic intentionally prints at most eight, it refuses any larger target plan rather than accepting a hidden or partial list.
- Minimum BRCB and URCB signal thresholds are caller-supplied. For a 6+6 physical scenario use 6 and 6; never infer missing signals.
- It cannot verify all APs were physically tested, packet-level causality or real-time freshness. No output authorizes merge, release, engine promotion, or retirement of R10 field authority.

## Tests, ownership and rollback

The new scripts/verify-p7-dual-ingress-diagnostic.py is read-only and uses Python standard library. The companion scripts/test-p7-dual-ingress-diagnostic.py has **16 sanitized negative/positive fixtures** and makes no network call. An early check in the existing SCL Interoperability R7 Windows CI runs these fixtures, with no new workflow and no additional native/.NET build.

Implementation lives in a new stacked **draft child PR** based on the exact P7.5 head. It does not commit onto P7.5 or other threads' branches. Software CI alone is CodeVerified only. The draft must not be merged into main until separately authorized physical test evidence is accepted.

Rollback: remove only the two scripts, this document and the small R7 path/fixture step; never change runtime/engine code to accommodate a diagnostic-only test.
