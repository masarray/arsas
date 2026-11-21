# ARSAS v1.6.44 — Verified P9 buffered SOE reporting

## Product scope
Public Windows x64 IEC 61850 engineering tester, not a SCADA system or an IEC 61850 conformance certificate.

## Changes
- Classify verified historical BRCB delivery as **Buffer event** in one Event column. Preserve **Data change**, **GI**, **Integrity**, **Quality** and other real IEC reporting reasons.
- Decode the original IEC MMS six-byte **BinaryTime** TimeOfEntry, validate exact raw bytes against rendered UTC, and retain strict IEC UTC-Time quality checks.
- Maintain per-RCB EntryID and association continuity; reject a retransmitted already accepted EntryID as a new buffered event.
- Retain BRCB history without any automatic PurgeBuf. The IED Browser exposes an **explicit, guarded, identity-verified PurgeBuf Write** only at operator request.
- Avoid repeating report-cursor timestamp parsing for every member of the same decoded frame; no extra MMS polling or unbounded per-frame caches.
- Preserve 77/77 selected SG7_BCU initial values and 4/4 routed configured static RCBs.

## Field evidence and limitations
- The 21 Nov 2026 SG7_BCU diagnostic showed **77/77** visible values, **4/4** independently routed BRCBs, **337** decoded report frames, historical A01/B01 `BRCB_REPLAY_PROVEN`, and visible **Buffer event** / **Integrity** / **Data change** separation.
- The same capture advertised **four BufOvfl** and **28 SqNum discontinuities**. Neither finding count is proof of a corresponding number of lost SOEs; **lossless recovery is not claimed**.
- Six-byte BinaryTime carries **no clock-quality byte**. Historic delivery classification requires an independent bounded age guard and valid BR/EntryID/reason metadata; it does not independently certify the relay's synchronization.
- The operator's observed field build precedes the final repeated-frame/EntryID regression-hardening commits. The final stable exact binary requires the normal packaging and device workflow retest; deterministic Windows CI does not substitute for broad relay interoperability.
- The ARIEC61850 engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` is unchanged from v1.6.43 and remains a documented **code-trial** pin. Physical R7/R10 qualification and multi-vendor service success are not claimed.
- Windows x64 binaries are unsigned with commercial Authenticode. Verify SHA-256 and follow site-specific authorization for IED Write/Operate/PurgeBuf.

## Release acceptance
Publication requires the exact merged-main SHA to pass **Build ARSAS**, the **validated Windows installer install/repair/uninstall smoke**, **CI P0 Workflow Integrity**, and the canonical release pipeline's SBOM/provenance/hash and sealed-package checks. Never publish a QA artifact under an existing stable tag or bypass mandatory distribution gates.

Previous stable release: [v1.6.43](https://github.com/masarray/arsas/releases/tag/v1.6.43). P9 integration owner: [PR #775](https://github.com/masarray/arsas/pull/775).
