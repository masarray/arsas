# C264 same-host simulator: context-aware GOOSE + static-report restoration plan
Status: **PLAN ONLY — draft, not a code fix or release**. Date: 2026-10-10. Owner: ARSAS consumer; semantic report decoder owner: ARIEC61850.

## 0. Locked facts, not assumptions

- User runs a local third-party IEC 61850 MMS simulator for a C264 IED **on the same Windows laptop** as ARSAS. Successful MMS association through local / virtual networking is real, but it does **not** prove a GOOSE Ethernet publisher is running or which Npcap adapter can observe multicast.
- The supplied diagnostic was captured with **ARSAS 1.6.40+1a364f8707f9f41612e6ce60e77620876eab4e78** and **ARIEC61850 1.6.40+8b0c0f7e695b016834e40a6f068994dcc945cb1a**. Commit 1a364f is a merge of the NTP/GUI trial source and the source tree is identical to consumer #501 HEAD 34d8f55f (verified by commit compare).
- MMS target: C264 at 192.16.1.13:102. Route-local interface: **KM_Test, Microsoft KM-TEST Loopback Adapter #2** with multiple IPv4 aliases. Treat the target, adapter GUID, source IP and capture device as different identities.
- Actual report evidence: 30 SCL DataSets, 364 static members; monitor requested 207 runtime points on 29 planned static RCBs; **134/207** values visible, **73/207 pending**, quality not supplied on **143** and **4/29** configured RCB targets had verified ingress at snapshot time. RCB plan being ready is not process-value proof.
- The log contains **REPORT_VALUE_REJECTED** for structured FCD payloads projected toward scalar C264CONTROL/* Mod.stVal, and for a Boolean status; also REPORT_RAW_STRUCT on LLN0.Mod/LPHD.PhyHealth. Those are specific evidence of *received but unprojectable* report values, not simply RCB silence. Never convert a Structure(...) string to a guessed scalar.
- SCL contains distinct prefixed MMXU logical nodes (rmsMMXU1 and powMMXU1); preserve prefix in every FCDA key and member ordering. SCL GOOSE dsGooseST contains 14 scalar FCDA members (seven exact stVal/q pairs). SCL GSEControl exists, but that alone is **not** proof of a running local Ethernet GOOSE publisher.
- Control command proof from same diagnostic: C264CONTROL/CSWI5.Pos successfully executed SBOw -> Operate -> positive CommandTermination -> closed process feedback. Preserve the entire working control path; never use unsafe commands for capture tests.
- User's four visible issues: editable IED IP field not centered; too many Unknown/pending SCL signals; select-all checkbox in fault record header misaligned with row checkboxes; GOOSE card CTA navigates correctly but selects no useful adapter and leaves an empty capture.

## 1. Verified source baselines / no destructive synchronization

Consumer repository: masarray/arsas, PR chain:
- #494 3cf821428efd954e415e7455acbd2936ff0019cd (whole-FCD WYE/DEL)
- #497 e0d63a9b8cd1dfb4b6c8321044363a283014b329 (DataSet-first Discovery)
- #498 67266f2017339159e0af4812e4c40339fadd5c70 (GOOSE CTA baseline)
- #499 a6d788d789eac4083ca7002b739b2f9d28d098a8 (typed GOOSE engineering inspector)
- #500 5e46dd562fc1f19f34eddc493b8c9872cb37bae6 (standalone SNTP)
- #501 34d8f55f499be94cc49539ba632aafa6efdef105 (NTP token/heartbeat polish)
All were draft/open when this plan was authored. Engine masarray/ARIEC61850 #157 HEAD 8b0c0f7e695b016834e40a6f068994dcc945cb1a, draft/open. ARSAS consumer HEAD #501 incorporates the earlier GOOSE/NTP work; app merge 1a364f has the identical source tree. Consumer #501 and main have diverged (at review: 176 ahead / 37 behind); do not reset, rebase, blindly cherry-pick or merge to main.

Golden Windows candidate before this plan: ARSAS #501 [Actions run 38020243949](https://github.com/masarray/arsas/actions/runs/38020243949), 1,467/1,467 application regression tests PASS plus portable publish and EXE smoke. That CI is *not* proof of physical C264 capture or full report completeness. Any implementation PR must build from the locked HEAD, show exact diff and test new Windows artifact. Always verify that engine lock resolves to #157 and that final executable's assembly diagnostics agree.

## 2. Architecture: separate four domains

1. **IEC 61850 MMS association**: endpoint + Windows unicast route + actual remote/local IP; may be a simulator in the same host process namespace.
2. **Ethernet GOOSE capture**: Npcap capture device, L2 multicasts/VLAN, actual transmitter and observed Ethernet frames; no automatic equivalence to an MMS interface or IP route.
3. **Static RCB reporting**: exact ordered DataSet/RCB and MMS InformationReport frames, typed schema-safe FCD -> scalar projection, GI/init and per-point value/quality evidence; never cyclic MMS process polling fallback.
4. **Presentation UI**: user-friendly states that do not hide uncertainty, no invented "Good" quality, cached per-session evidence and bounded render updates.

Do not conflate "simulator same PC" with a multicast publisher, or "SCL model" with live values.

## 3. Implementation packages and acceptance gates

### P0 — preserve evidence and create deterministic replay (MUST precede fixes)
- Read supplied C264 IID and sanitized diagnostic offline. Use an **internal/test-only synthetic fixture** for exact model shape, 14 paired GOOSE FCDAs and structured 4-child Mod value. Do not commit user-supplied raw SCL, workstation names, MACs, network inventory, unredacted captures or privileged control logs to a public repository without explicit consent.
- Capture separate safe facts: SCL fingerprint, DataSet/FCDA order including lnPrefix, RCB identity, exact type + raw MMS structure, quality/timestamps, and source ingress. Add test expectation matrix per signal: model-known, RCB-ready, report-observed, projection-valid, quality-supplied.
- Freeze baseline tests of the successful control path, Offline Open SCL, Dataset-only Static Reporting, Discovery consistency, SNTP standalone and prior GOOSE UX before any code change.

### P1 — two narrow UI corrections (consumer only)
- \`IpConnectWizardWindow.xaml\`: editable \`RelayIpBox\` needs **horizontal AND vertical center** in both selected display and editing mode. Apply to the editable template's \`PART_EditableTextBox\` (TextAlignment Center, vertical content alignment Center); preserve caret, manual typing, recent list, focus, validation, DPI and submit. Do not only center the ComboBox outer border. Apply consistent treatment to PC-IP dropdown if it shares the issue.
- \`FaultRecordWindow.xaml\` + \`FaultRecordWindow.HeaderSelection.cs\`: select-all header checkbox and row checkboxes must share a **single full-width centered checkbox column template**; override the default header/cell 9px padding **only for that column**, not all grid columns. Pixel/DPI parity at 100/125/150%, enable/disabled/tri-state, keyboard and bulk-select. No arbitrary hard-coded left offsets.

### P2 — engine-owned typed static-FCD projection (HIGH PRIORITY, isolated)
- Engine: \`src/AR.Iec61850/Mms/MmsSemanticReportValueProjector.cs\` and \`MmsReportValueProjector.cs\`; consumer integration: \`Services/NativeIec61850Client.SemanticReporting.cs\`, \`Services/ReportProcessValueSafety.cs\`, \`Services/NativeIec61850Client.StaticDataSetReporting.cs\`.
- Reproduce \`REPORT_VALUE_REJECTED\` from a **typed four-child structured FCD**, not a rendered \`Structure(4)\` string. Map to the exact SCL/online-verified FCDA child using ordered DataSet member identity, LD, **prefix + lnClass + lnInst**, FC, doName and daName; respect optional members and nested structures. Establish strict one-to-one field type; extract stVal/q/t only when the schema and wire type agree. Return structured proof / explicit reject reason; do not discard base wire evidence.
- Beware a missing RCB/GIN/first report versus a projection rejection: they require different remediation. Test 29 RCB target states and per-point updates; do not mark Quality=Good without actual q and do not auto-fill missing q. Existing report-only mode must remain **zero cyclic MMS polling and zero dynamic DataSet writes**, even when 73 values are pending.
- No silent "SCL reparsing" that changes confirmed DataSet order. No control safety regressions. Engine fix, if needed, lives in engine PR on top of #157 with independent tests; consumer updates explicit lock atomically and only after passing combined CI/field evidence.

### P3 — same-host/simulator-aware GOOSE NIC resolution (consumer; new policy service)
- Existing failure: \`MainWindow.IedGooseQuickStart.cs\` uses UDP route-to-port-102 to infer local NIC; \`LooksLikeLoopback\` rejects *every* adapter with loopback in its label. \`MainWindow.GooseSubscriber.cs\` then leaves selection null and shows a blank viewer. On KM-TEST virtual interface this is a known usability failure, but **starting an arbitrary physical interface is not the solution**.
- Implement a pure \`GooseCaptureContext\` / \`GooseAdapterCandidate\` resolver. Classify local target IP (local address enumeration), virtual KM-TEST NIC, Windows route, Npcap named capture devices/GUIDs/MACs, actual loopback capture device and physical adapters separately. Prefer exact GUID identity, then verified MAC; don't equate "only NIC remaining" with correct multicast capture.
- If the MMS target address is local: clearly identify "Same-PC simulator" and treat the routed NIC only as a weak hint; loopback capture is eligible **only if this capture backend can actually receive required Ethernet frames**. A Windows IP loopback success cannot prove Ethernet GOOSE at L2.
- On CTA: open GOOSE tab, attempt a **bounded and cancellable** read-only adapter discovery/probe (on explicit click only, with cap on candidates/time/packets; no indefinite Npcap handles, uncontrolled parallel capture or unsolicited command/control writes). Confirm actual GOOSE EtherType, accepted VLAN, APPID/GoCB/DataSet identity before selecting the proven adapter; reuse the existing engine decoder. If no proven adapter or no GOOSE frame observed, show a concise actionable empty state: "MMS connected; GOOSE publisher not observed. Choose an adapter or check that simulator emits GOOSE." Expose manual Npcap adapter choice and retry; never lie "Monitoring GOOSE" at 0 frames.
- On physical IED: unique Windows-route interface may remain a default *candidate*, but actual frame evidence upgrades confidence. Cache **last successful** adapter per stable device/interface fingerprint, not per IP alias alone, with invalidation on device removal/rebind; do not persist auto-guesses as verified.
- Observability: capture attempted adapters, excluded reason, actual frame counts, publisher identity, probe timeout and selection confidence in diagnostic logs. Keep Npcap worker/disposal bounded, UI callbacks coalesced. Confirm whether the local simulator really publishes GOOSE using an independent sniffer before declaring any code bug if all adapters see zero frames. A GSEControl in the IID is metadata only.

### P4 — observable report/UI truth (consumer)
- In Live Signal Values distinguish Pending (no report ingress), Rejected (structured unprojectable), Not configured (no report-backed source), and Quality not supplied, with terse hover detail and counters. Don't convert Pending to 0/Unknown just to populate a grid.
- Show aggregate e.g. "134/207 values received · 73 awaiting reports · 4/29 RCB verified"; update through existing async coalescing, not a per-row polling timer. Existing exact static RCB routing facts remain authoritative and preserved.
- GOOSE tab: if no frames, explain **why** and offer adapter picker/test of publisher. Avoid the empty inspector with no context. Keep working six-column Messages and virtualized Signal/Value/Quality inspector from #499 intact. Don't degrade standalone SNTP #500–501.
- Examine fault-record check-column alignment with user screenshot while keeping download selection semantics unchanged.

### P5 — qualifying tests before any merge/release
- Unit: same-PC detection, KM-TEST loopback versus NPF_Loopback, exact Npcap GUID matching, ambiguous multiple NIC aliases, no-GOOSE publisher, actual GOOSE frame match, cancellation/timeouts, old adapter invalidation.
- Engine: structured FCD whole-object expansion, 4-child Mod, companion q/t, two prefixed MMXU instances, wrong member order, incomplete/conflicting schema rejection.
- Consumer: no cyclic MMS fallback, no dynamic writes, 134/207 counts not overwritten, status/quality trust, verified RCB ingress updates, no duplicate capture listeners, working positive control chain.
- GUI: editable IP centered in read/edit modes, checkbox header/row centered at 100/125/150%, GOOSE CTA selected tab and meaningful no-frame state, compact ARSAS styling.
- Integration: local simulator MMS works, separate GOOSE emission proven/absent independently, optional physical IED capture, no false positives. Capture bounded memory/time statistics and actual exact-head Windows portable artifact.
- Release gate: keep all PRs DRAFT until physical sign-off; do not merge/rebase onto main or change public release based only on green CI. Keep old known-good artifacts accessible and engine lock unchanged until dual-repo proof.

## 4. Change control and sequencing

Ship P1 UI fixes independently; P2 engine with tests before consumer report integration; P3 as a separate capture-context policy module; P4 UX only after observed evidence; P5 final proof. Avoid mixing these into one giant code diff. PRs must target verified stacked branch, list precise file changes, maintain exact baseline diff, and avoid off-scope refactoring. Plan changes can be merged independently **only after** review; this document is not an authorization to promote the code.

## 5. Evidence provenance and hard unknowns

- Observed: ARSAS reports same-PC IP route through a Microsoft virtual loopback interface; GOOSE view is empty.
- Unknown: whether this simulator build is configured to publish Ethernet GOOSE and whether Npcap can observe its frames on KM-TEST or NPF_Loopback; verify before claiming an adapter bug alone.
- Observed: per-target report ingress 4/29 and rejected \`Structure(...)\` scalar updates.
- Unknown: how many of 73 pending are caused by missing RCB report emission, unprojectable whole-FCD payloads, or missing first GI; measure separately.
- A model declaring a signal or RCB is not evidence of live traffic; diagnostic snapshots are time-bound.
