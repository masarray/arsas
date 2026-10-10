# Copy-paste handoff — ARSAS C264 local-simulator continuation

You are continuing a **two-repository, Windows WPF / IEC 61850 electrical-engineering tool** project. Work in GitHub, not by replacing it with a web app. The latest request is to **implement the audited plan for C264 same-host simulator, report Unknown/pending values, GOOSE capture adapter intelligence and two GUI alignment bugs**, without losing any previous progress. Read [C264 same-host repair plan](./C264_SAME_HOST_REPAIR_PLAN_2026-10-10.md) in full **before editing code**. Treat that plan as technical source of truth but VERIFY actual current GitHub refs, source and CI at start; do not treat stale snapshots as immutable GitHub state.

## Repository and locked baseline
- Consumer: https://github.com/masarray/arsas ; planned doc PR on top of **#501** (head \`34d8f55f499be94cc49539ba632aafa6efdef105\`). Running build diagnostic uses merge commit \`1a364f8707f9f41612e6ce60e77620876eab4e78\`, **tree-identical to #501**, so no functional code divergence.
- Engine: https://github.com/masarray/ARIEC61850 ; **#157** head \`8b0c0f7e695b016834e40a6f068994dcc945cb1a\`.
- Stacked consumer drafts: #494 whole-FCD measurements, #497 DataSet-first Discovery, #498 CTA capture behavior, #499 engineering GOOSE Inspector, #500 standalone SNTP, #501 compact NTP header token/heartbeat. These have NOT been promoted to main. At last audit #501 vs main was diverged (+176/-37); do not force merge/rebase.
- Last exact-head Windows build for #501: https://github.com/masarray/arsas/actions/runs/38020243949 (1,467/1,467 application tests PASS, portable smoke PASS). No physical qualification implied.
- User wants optimized managed C#/.NET software, not new unmanaged/native implementations, not naive string/regex heuristics, not bloated GUI/cards, and no regressions.

## Actual evidence and major findings
- User runs a C264 IEC 61850 server **on the same laptop**. MMS target 192.16.1.13:102 routed to Windows \`KM_Test\` (Microsoft KM-TEST Loopback Adapter #2 with multiple IPv4 aliases); successful MMS communication and a validated positive C264CONTROL/CSWI5.Pos SBOw→Operate→CommandTermination→Closed feedback must remain intact.
- Screenshot GOOSE CTA enters GOOSE tab, but adapter is unselected and counters stay zero. Source: \`MainWindow.IedGooseQuickStart.cs\` route-based NIC inference and broad \`LooksLikeLoopback\` filter reject simulated local virtual interface. Do NOT assume that same-host MMS traffic implies L2 GOOSE publisher: verify actual EtherType/frames; zero frames could be simulator configuration.
- Open SCL diagnostic has 30 DataSets / 364 members, 207 selected report-only points from 29 RCB plans, 134 received values, 73 pending, 143 quality not supplied, only 4/29 exact RCB targets showing ingress. Many rejected whole-FCD structures (\`REPORT_VALUE_REJECTED\` for Mod.stVal etc.) and \`REPORT_RAW_STRUCT\`. The true issue is split between missing report ingress and unsafe structured-to-scalar projection. Never fake values/Good quality or enable cyclic MMS process polling.
- The C264 IID has 14 ordered GOOSE FCDA entries = seven \`stVal\`/\`q\` pairs. It includes distinct \`rmsMMXU1\` and \`powMMXU1\` prefixes. Preserve LN prefix/FCDA order and type. **The user-provided IID/diagnostic/captures must not be uploaded unredacted to the public repos without permission.**
- UX fixes: \`IpConnectWizardWindow.xaml\` editable IED IP field requires horizontal+vertical center *including PART_EditableTextBox*; \`FaultRecordWindow.xaml\` and \`FaultRecordWindow.HeaderSelection.cs\` header select-all checkbox must align exactly with row checkbox; header uses 9px padding and should align via column-specific shared template.

## Execute in a safe order
1. Verify GitHub current PR HEADs and engine lock, exact source/tree ancestry; read repo guides (\`docs/WORKSTREAM_COORDINATION.md\`, \`docs/static-dataset-report-only-contract.md\`, \`docs/GOOSE_SUBSCRIBER.md\`) and the full planning document.
2. P0 build deterministic **sanitized synthetic** test fixtures and map report missing versus report rejected; lock already verified control/GOOSE/SNTP/Discovery tests.
3. P1 deliver narrow editable-IP and Fault Records checkbox alignment patch + WPF DPI tests.
4. P2 research/fix typed schema-safe structured FCD projection in ARIEC engine (files \`MmsSemanticReportValueProjector.cs\`, \`MmsReportValueProjector.cs\`) and consumer \`NativeIec61850Client.SemanticReporting.cs\`. Prove each derived scalar and quality maps to exact ordered SCL FCDA; reject conflicts. Update engine pin intentionally only after its unit tests and consumer tests pass.
5. P3 build context-aware GOOSE resolver (actual local target classification, exact Npcap GUID/MAC mapping, virtual/physical/loopback distinction, bounded explicit-click probe with verified GOOSE frame evidence, manual fallback), without guessing a working adapter or assuming simulator publishes GOOSE. Actionable no-frame UI, correct counters.
6. P4 safe observable Live Values UX: Pending vs report-rejected vs missing q; no invented quality; keep compact virtualized GOOSE table. Keep managed async workers/limited capture, event coalescing, cancellation, disposals.
7. P5 Windows CI + portable EXE smoke on **exact final HEAD**, field-test user simulator and optionally real IED; validate reported 4/29 and 73 pending classification, no dynamic DataSet writes/polling, full command safety, and single adapter capture. Leave all code PRs draft without real acceptance.

## Work disciplines
- Do not rewrite or downgrade ARSAS into a web application. Do not silently change public release, main, existing engine pin, or remove important work.
- No broad regex projection of untyped report values; use canonical typed engine-owned authoritative schema and fail closed.
- Distinguish observed source behavior from hypotheses. Reconcile open SCL → static reporting and Discovery → static reporting in the same model/RCB path.
- For every issue provide smallest reproducible failing test, root cause, targeted patch, affected files, expected protocol behavior, performance/resource impact, tests, CI run and exact artifact. Do not claim completion before actual CI.
- Use user-visible progress periodically and proactively continue implementation. Do not stop at planning if the next thread is asked to **work on fixes**.

## First response in the new thread
Report current verified repo heads, which locked baseline you will use, the leading root-cause hypothesis for same-host GOOSE and report projection, then begin P0/P1 from the plan rather than repeating questions already answered.