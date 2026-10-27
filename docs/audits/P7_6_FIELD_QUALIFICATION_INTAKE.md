# P7.6 — Read-only dual-ingress field diagnostic intake (draft)

Parent: issue #446. D22 restores the old #479 diagnostic intake as an isolated utility on integrated RC; historical #476 is already retired. No Studio runtime, MMS engine, IED association, RCB planner, native installer, physical R10 or release authority changes.

## Purpose

P7.3 records semantic Discovery/Open-SCL fingerprints; P7.4 separately tracks actual routed values on each exact static BRCB and URCB target. The native Studio Copy Diagnostic truncates individual RCB target lines to eight per ingress. Semantic MATCH alone is not traffic proof.

This milestone adds a fail-closed local validator of one **full native Studio Copy Diagnostic** for exactly one IED. It requires a matching SHA-256 fingerprint in both independent ingress summaries, explicit dual-ingress traffic qualification, nonzero selected static BRCB and URCB coverage, zero uncovered signals, an unambiguous complete per-ingress target inventory, accepted ROUTED status per distinct exact RCB, and a timezone-aware first routed timestamp. It rejects missing/duplicate IED sections, emergency reports, mismatched ingresses, empty or ambiguous target identities, any target inventory truncated beyond eight and wrong selected AccessPoint.

Its JSON output is explicitly DIAGNOSTIC_CONSISTENCY_PASS_NOT_PHYSICAL_VERIFIED. Both physicalVerification and wirePcapVerification are **false** regardless of an otherwise valid diagnostic. Offline text can be copied or forged; this validator is not independent wire or physical verification.

## D22 RC-aligned hardening

The validator is matched against the current native `DiagnosticReportBuilder.AppendStaticIngressParity` and `StaticAcquisitionIngressEvidence` output. It requires one explicitly selected SCD AccessPoint, both ingress families, exact semantic SHA, exact per-target ROUTED evidence, and a complete bounded target inventory. An IED with more than eight planned targets cannot qualify from truncated Copy Diagnostic text.

Read-only text is capped at 8 MiB. Results use atomic replacement; a failed recheck overwrites any earlier PASS with `qualification=NOT_ESTABLISHED` and false physical/PCAP flags. Do not upload private real IED reports to GitHub. No network, MMS write or physical authority is introduced.

## D24 — Source-consistency hardening

Before passing a local copy, the validator now reconciles **each ingress** per-family BRCB/URCB `bindings=` totals with `staticBRCB`/`staticURCB` counters, matching `StaticAcquisitionParityTracker.BuildEvidence` rather than trusting either number in isolation. Binding counts are incidences; they must **not** be equated to distinct requested IEC points because DataSets/RCBs may overlap. Every numeric diagnostic field is bounded to ASCII digits of at most ten characters before `int()`, so malformed 5,000-digit values fail cleanly and cannot leave a stale PASS output. Negative tests cover both ingress families, long header/traffic/binding numbers, non-ASCII digits, and CLI PASS→NOT_ESTABLISHED replacement. These guards add no IED, network, protocol or physical authority.

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

The new scripts/verify-p7-dual-ingress-diagnostic.py is read-only and uses Python standard library. The companion scripts/test-p7-dual-ingress-diagnostic.py now has **24 offline unittest methods** with sanitized positive/negative fixtures, including D24 review regressions. The existing Ubuntu `CI P0 Workflow Integrity` runs the tests; no new workflow or additional native/.NET build was introduced by this validator. Exact-head GitHub Actions evidence is still required after the D24 source change.

Implementation is isolated in [PR #759](https://github.com/masarray/arsas/pull/759), based on the protected integrated RC. Owner review, required-check visibility and physical IEC 61850 acceptance remain separate. Do not merge into the RC/main or promote stable based only on text consistency or PR-green CI.

Rollback: remove only the two scripts, this document and the small R7 path/fixture step; never change runtime/engine code to accommodate a diagnostic-only test.
