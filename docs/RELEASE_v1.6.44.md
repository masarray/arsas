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
- The SG7_BCU screenshot and diagnostic came from the earlier P9 field-test build. Additional accepted-EntryID hardening followed before v1.6.44 publication; its exact final binary passed Windows packaging checks but is **not claimed as separately field-retested** on SG7_BCU. CI cannot replace broader relay interoperability qualification.
- The ARIEC61850 engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` is unchanged from v1.6.43 and remains a documented **code-trial** pin. Physical R7/R10 qualification and multi-vendor service success are not claimed.
- Windows x64 installer and portable EXE are **not commercially Authenticode-signed**. Verify the published SHA-256 and follow site-specific authorization for IED Write/Operate/PurgeBuf.

## Verified public distribution — 21 Nov 2026
- Public stable: [v1.6.44](https://github.com/masarray/arsas/releases/tag/v1.6.44), exact source commit `e5dda6a2e0ffc4e4581cdab8d917bea9861a3b8c`, not a repackaged QA artifact.
- [Exact-main Windows build](https://github.com/masarray/arsas/actions/runs/44871578489), [Windows installer smoke](https://github.com/masarray/arsas/actions/runs/44871578492), and [official release workflow](https://github.com/masarray/arsas/actions/runs/44871578462) all completed successfully.
- Installer SHA-256: `0a6b7cabdfe3d73647dcd5960f35b4f9b94f8ffa275daf3cb88fdc3a9804c2a2`.
- Portable SHA-256: `942ae62e961062dd5d603232da942069c72138e6361235f01426dc1431a67a62`.
- Checksum file, SPDX SBOM and provenance are attached to the public tag. These establish inspectable package identity, **not** formal IEC 61850 conformance or lossless SOE.

Previous stable release: [v1.6.43](https://github.com/masarray/arsas/releases/tag/v1.6.43). P9 integration owner: [PR #775](https://github.com/masarray/arsas/pull/775).
