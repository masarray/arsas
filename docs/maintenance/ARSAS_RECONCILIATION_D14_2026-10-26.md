# ARSAS D14 — reconcile parallel D13 advancement; audit/close historical operator Material geometry #583

2026-10-26 Asia/Jakarta. Continued from isolated D13 checkpoint `85ea8008d74b8d6a943c3609ecd308c933bfc0c6` and D12 `0107f8ceeb187bf9899c6c8faf33b7ffd47ac98e`. **No stable, main, RC, #753 or #754 source branch write.**

## Preflight: active parallel engineering was NOT overwritten

Initial D14 found integration draft #755 already advanced in parallel by **two commits** after D12 `b52d4824bbadb04dc0d897b476b38bcf507cda38`: HEAD `7d235554225aa0341561b4e887a852fb6fd8e269`, exact 12 paths, ahead5/behind0 frozen RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`. New files changed in two commits: `MainWindow.xaml.cs`, `Services/Iec61850ControlInspectionPublication.cs`, `Services/UiResponsiveIec61850MonitorRuntimeFacade.cs`, `tests/ARSAS.Tests/M217P0ControlSingleFlightRecoveryTests.cs`. D13 added post-Write model-epoch gate and stale GUI exception suppression. **Do not transplant historic D12 or replay partial updates onto the new HEAD.**

Exact current HEAD verification:
- Windows [40325732039](https://github.com/masarray/arsas/actions/runs/40325732039): **completed SUCCESS**.
- SCL [40325732197](https://github.com/masarray/arsas/actions/runs/40325732197): **completed SUCCESS**.
- IEC interoperability guard [40325732127](https://github.com/masarray/arsas/actions/runs/40325732127) and P1 advisory [40325732341](https://github.com/masarray/arsas/actions/runs/40325732341): **completed SUCCESS**.
- #755 OPEN DRAFT, #753 GOOSE `0652f66bed0c72dded20ff547e23e2ef1989341c` and #754 isolated Command `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d` unchanged. Protected RC and public stable v1.6.42 never promoted.
- CI green does not establish physical IEC 61850 Operate, GOOSE, SGCB or R7/R10 FAT. #615, #606 and #727 owner safety gates remain open.

## Concrete D14 reconciliation: #583 CLOSED UNMERGED after exact source and tests

[#583 individual audited closure comment](https://github.com/masarray/arsas/pull/583#issuecomment-6416777692). Historical HEAD `756de98a2bb0e70bf4f88199617c165632986ddb` ancestor to RC ahead1363 / behind0; source behavior **not presumed from ancestry**. All three old changed paths checked old→current RC:
1. `App.xaml`: old `4f1233e9cb0d5788f0a1d7455c69327dfa98c9cf` → RC `d2fe57403412db2c076e944470ae6c0bb9540186`. All original operator Material Icon geometry resource keys retained exactly once for topology, diagnostics, command power, Add IED, NTP and MaterialIconGlyph style; current file grows with unrelated GUI resources.
2. `MainWindow.xaml`: `3ae178e957de02adf22fce87214240677cc1c940` → `ee28dfe80e011740f72d114522f4311f8d852879`. Original navigation glyphs, eight NavButton_Click event handlers, Goose nav and Command Dock header bindings retained. `MaterialIconGlyph` count moved 22→21, but exact historical assertions remain and source structural owner verified. Not claiming whole-file identity.
3. `tests/ARSAS.Tests/MaterialIconConsistencyTests.cs`: `f663cc37ad343d39d825e38243fd0a1165679e26` → `4291628ff65bd7bdebb340297004aa47111be5f5`. All 8 original regression methods preserved, and all **52 historical Assert statement lines byte-identical**; tests cover operator icon material geometry, navigation handler identity, dynamic Add IED, P2 Industrial, command toolbar, NTP clock and Apache Material icon license. No lost original unique behavior. Newer canonical UI/test additions remain.
**Outcome:** stale historical #583 tracking PR **CLOSED UNMERGED** after individualized evidence. Branch retained. No source cherry-pick or RC mutation. No physical device claims.

## Live inventory / next gates

At D14 start 116 open PR, 17 independent issues; after #583 historical closure **115 open PR, 17 independent issues** (GitHub open pulls 100+15; GitHub issue list 100+32, true issues 10+7 after filtering PRs). Historical closures 41→**42**, all unmerged in the running D1–D14 campaign. No new PR created D14.

Only accepted scope completed this batch: parallel-owner synchronization, source+test audit and one safe historical closure. #599 RCB fold/default, #595 remote files, #582 GOOSE SCL owner, phasor #577/#570/#567, RCB #594 remain unresolved and must not be mass-closed. #753/#754/#755 draft safety integration needs real owner review and physical qualification before RC. D15 should prioritize real source fixes on isolated HEAD only when new hazard is verified, and follow first by checking current branch SHA to avoid parallel owner conflicts.

Safe roots remain: ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2`, ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966`, RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, trial engine `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, #726 frozen Write, #716 Boolean, #688 SG7 and #732 IP GUI unchanged.
