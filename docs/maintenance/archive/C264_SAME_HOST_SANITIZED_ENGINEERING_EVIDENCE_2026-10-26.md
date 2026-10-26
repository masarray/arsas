# C264 same-host IEC 61850 — sanitized engineering evidence archive
**Archived:** 2026-10-26 Asia/Jakarta. **Source observation:** 2026-10-10.  
**Status:** HISTORICAL FIELD/ENGINEERING EVIDENCE + OPEN QUESTIONS, **not** an accepted release, merged source code, or a claim of physical GOOSE/report completeness.

This archive preserves the distinct engineering findings and implementation/acceptance obligations of historical [ARSAS #502](https://github.com/masarray/arsas/pull/502) and [#503](https://github.com/masarray/arsas/pull/503) while omitting workstation addresses, capture interfaces/identifiers and user-supplied unredacted IID/PCAP/logs. Original three document blobs **remain recoverable on the preserved PR branches**; do not publish underlying bench captures or private endpoint values. Nothing here enables a command or qualifies a physical IED.

## 1. Exact provenance: three unique source documents

| Historic PR / head | Path and original blob | Separate unique contribution |
|---|---|---|
| [#502](https://github.com/masarray/arsas/pull/502), head `46639977ab104b9201dfdd65af0b92f37b06d627` | `docs/field-trials/C264_HANDOFF_PROMPT_2026-10-10.md` — `f71458a87150936a4446c091cb73c89f4b0c790e` | Concise operator handoff, historical branch/CI pins, preserve known-positive Control path and independent P0–P5 sequencing |
| [#502](https://github.com/masarray/arsas/pull/502), same head | `docs/field-trials/C264_SAME_HOST_REPAIR_PLAN_2026-10-10.md` — `79095cfc289f9a0987756b140cd2caf0ed713f20` | Full four-domain architecture, typed report + GOOSE NIC recovery plan, hard uncertainty and acceptance gates |
| [#503](https://github.com/masarray/arsas/pull/503), head `4613838f608152a263b54adb84d07f85155a43cf` | `docs/C264_SAME_HOST_FIELD_HANDOFF_2026-10-10.md` — `9a9ef262cc046b9548bd73821b5ece46527478d6` | Distinct GUI/Explorer diagnostic checklist: 7-versus-6 column contract suspicion, editable ComboBox centering, checkbox geometry, adapter misclassification |

Do not confuse *open/draft* status on the October 10 snapshot with status today. Historical #504 was a narrow P1 UI implementation on #501; its old testing and [Actions run 38020243949](https://github.com/masarray/arsas/actions/runs/38020243949) prove only **historical exact-head automated tests**, not C264 field acceptance. #504 was subsequently administratively reconciled in the D1/D2 ancestry lineage, without retroactive physical guarantees.

**Original historical references:** consumer #494/#497/#498/#499/#500/#501 (multiple stacked drafts), engine #157 `8b0c0f7e695b016834e40a6f068994dcc945cb1a`. Do not repin current RC to these stale heads. Their old source is not the current authority. See [reconciliation D2](../ARSAS_RECONCILIATION_D2_2026-10-26.md) for live snapshot and current trial engine lock.

## 2. Observed facts vs hypotheses — safe abstracted bench evidence

Historical bench used a **third-party C264 software IEC 61850 simulator and ARSAS on the same Windows PC**. MMS succeeded over a local/virtual networking interface, but a successful TCP/MMS association is **not** evidence of Ethernet GOOSE multicast. The original target, local source, route identity, virtual adapter, Npcap device GUID, MAC, raw capture and filenames are intentionally excluded.

Verified-at-snapshot observations from the historical diagnostic:
- SCL inventory: **30 DataSets, 364 members**; static report-only acquisition requested **207 process points and 29 RCB plans**.
- **134/207** received/displayed, **73/207** pending, **143** quality not supplied, **4/29** configured RCBs showed exact inbound Report evidence at that instant. These are one time-bounded diagnostic snapshot, not current production counters.
- Received structured FCD values sometimes triggered `REPORT_VALUE_REJECTED` / `REPORT_RAW_STRUCT`, including an LLN0-mode/status path. This is **not equivalent** to no RCB ingress: received-but-unprojectable, absent ingress, no GI/first Report and unsupported q must be distinguished.
- IID described 14 ordered GOOSE FCDA entries — **seven exact `stVal`/`q` pairs** — including distinct prefixed MMXU logical nodes. Original unique instance prefix and FCDA sequence are semantic identity; this document deliberately avoids private identifiers.
- A **separately verified positive C264 position-control sequence** completed SBOw→Operate→CommandTermination→Closed feedback on that historic bench. This existing safety contract must not regress. This fact does not authorize new commands.
- GOOSE UI navigated to capture but had no selected adapter and zero frames. The old `LooksLikeLoopback` substring exclusion could reject a virtual test adapter; this was a **plausible UX-root-cause hypothesis**, not proof that the simulator emitted a GOOSE frame.
- GUI: editable IED IP text was off-center, Fault Record select-all header checkbox did not visually align with row checkboxes. A separate finding suspected six-column Explorer layout logic against seven visible headers; verify on current UI before assuming it still exists.

## 3. Four independent authority domains — do not conflate

1. **MMS unicast session:** IEC association, remote endpoint, Windows routing, active local address and verified native control authority. Not a GOOSE packet source selector.
2. **GOOSE Ethernet observation:** Npcap capture identity, actual L2 EtherType 0x88B8/VLAN, verified publisher/APPID/GoCB/datSet/confRev and timeAllowedToLive. A declared SCL GSEControl is NOT received traffic. Never guess an FCDA identity from raw allData position.
3. **Static RCB reporting:** exact ordered FCDA/FCD → typed semantic projector, RCB membership/ownership, GI/report ingress, companion q/t and association. No fabricated Good quality, scalar-zero fallbacks, cyclic MMS process polling or dynamic DataSet writes.
4. **WPF/operator presentation:** bounded, virtualized and provenance-labelled status. Distinguish Pending, missing ingress, rejected typed projection, no q, stale, and actual report-backed values.

## 4. Unique engineering recovery tasks (historical, verify against current implementation)

### P0 — evidence first
- Retain only **synthetic sanitized** test cases for ordered 14-member GOOSE FCDA, typed four-child FCD structures and expected report-reject classifications. Raw user IID, diagnostic dumps, network addresses and privileged control traces stay out of public source.
- Record exact model/RCB/member identity, typed raw response, quality/time, association and event causal provenance. Never infer live evidence from SCL metadata.
- Protect positive accepted control, GOOSE, SNTP, F650/whole-FCD and SCL behaviors with regression tests.

### P1 — scoped WPF UI
- Editable IP ComboBox `PART_EditableTextBox`: center text horizontally/vertically in display **and editable** modes. Preserve caret, focus, paste, recent list and DPI 100/125/150%.
- Fault Records first DataGrid checkbox column: align select-all header and row checkboxes using column-local template, not arbitrary global padding; preserve tri-state, eligible-row bulk selection and download semantics.
- Explorer: verify six-column search heuristic versus seven visible headers before any width change. This was a suspicion from the historical source, not a known current regression.

### P2 — typed report projection in the engine
- Use actual structured `MmsDataValue` and ARIEC-owned ordered typed schema, never parse `Structure(4)` text or guess `stVal`/q/t by visual display string.
- Resolve one exact DataSet member and nested DO/DA with LD, LN prefix/class/instance, FC, type, member ordinal and RCB context; no sibling phase bleed, no fallback treating unset q as Good.
- Distinguish missing RCB/GI/ingress from **observed but rejected** structured member. Keep report-only static acquisition with zero cyclic polling and zero dynamic mutation.
- Engine change only when proven and with exact new pinned SHA plus consumer bridge/CI tests. Historical #157 is not today's authorized runtime pin.

### P3 — context-aware GOOSE NIC discovery
- Distinguish real Npcap loopback pseudo-device from Windows virtual test adapter and physical NIC. Do **not** blacklist every friendly name containing “loopback”; do **not** auto-pick arbitrary NIC.
- Use exact Npcap adapter GUID/MAC or explicit verified capture evidence. Local MMS route is a hint at most. On explicit operator CTA, bounded/cancellable read-only candidate probing is acceptable subject to backend capability; no uncontrolled parallel sniffers.
- Select adapter only from verified GOOSE L2 evidence. If no EtherType/APPID/GoCB/frames, show actionable “MMS connected; GOOSE publisher not observed” status and allow manual adapter/retry. Never advertise active subscriber success at zero verified frames.
- Keep capture lifecycle bounded and source-scoped, no new control write or suspect default packet binding.

### P4 — truthful report/GOOSE UI
- Report counter/state distinguishes no report, received-but-rejected, quality missing, successfully projected, stale. Record exact RCB causality; 134/207 and 4/29 were **historical** sample numbers, not target values to manufacture.
- Preserve compact GUI and existing virtualized rows; avoid timer-per-row polling/allocations or independent MMS decoder implementations.
- GOOSE empty state and adapter selection must disclose verified observation versus default guess.

### P5 — independent validation
- Unit: wrong/ambiguous NIC, virtual test adapter versus pseudo-loopback, timeout/cancel, no transmitter, unbound GOOSE order/FCDA refusal; typed FCD root + q/t, wrong member/FC, prefixed MMXU; WPF centering/checkbox geometry.
- CI: exact-commit .NET test, native bridge pin and Windows portable build; compare current required checks (not merely one green job).
- Bench: prove simulator GOOSE emission independently from MMS; verify exact RCB GI/Report and per-point quality, and optional real IED evidence.
- Physical and release authority: **never infer R7/R10/IEC 61850 field acceptance from CI, simulator or this archive**. Current #729 remains separately gated; accepted stable v1.6.42 immutable.

## 5. Archival disposition and work ownership

The three source documents were **planning/handoff evidence**, not standalone tested application code, so a sanitized canonical archive must **never** be interpreted as code integration. This archive preserves all separate engineering decisions and bounded observations necessary for re-triage; historical original blobs and PR branches are deliberately retained for reversible provenance.

Before closing #502/#503, verify this archive exists at a permanent branch SHA, cite exact source blob SHA, and leave individual PR comments with **Disposition, Canonical Replacement, Unique Delta, Safety, Follow-up**. If a reviewer identifies a concrete omitted unique semantic assertion, reopen/repair the archival handoff before treating it as superseded. Do not forward old private endpoint or raw captures to public checkpoints.
