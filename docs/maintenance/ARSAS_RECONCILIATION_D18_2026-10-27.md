# ARSAS D18 — evidence-gated PR retirement, CI-cost gap and field-provenance handoff

**Date:** 2026-10-27 Asia/Jakarta. Continuation of [D17](ARSAS_RECONCILIATION_D17_2026-10-26.md), inherited audit HEAD `b71e61b18d71d3502fd09e318dfe5e04ed73ae49`. **NOT a code/release/engine/main merge or physical FAT**.

## Protected references
- ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Integrated RC #727/#729 at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, protected OPEN DRAFT (not merged to main).
- Current RC trial engine `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` only, no repin.
- Safety drafts #753 `0652f66bed0c72dded20ff547e23e2ef1989341c`, #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, #755 `7d235554225aa0341561b4e887a852fb6fd8e269`, stacked #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78`. Also protect #726 Write, #716 Boolean, #688 SG7, #732 IP GUI. Stable v1.6.42 remains published and unchanged.

## Three GitHub PRs actually closed D18 (closed UNMERGED, after reasoned audit comments)
1. [#595](https://github.com/masarray/arsas/pull/595#issuecomment-6426828627) COMTRADE per-record File Browser GUI:
   - HEAD `8d29512f8a3adb2b039b8967ec1f938f40e50f75`, RC ancestor `behind=0`.
   - All 7 original paths present; 2 byte-identical Git blobs (`Models/IedBrowserRemoteFile.cs`, `tests/ARSAS.Tests/M2IedBrowserFilesTests.cs`), 5 evolved.
   - RC `IedBrowserFileCatalog.DisplayRows` still enforces one bundled record per engine-proven complete CFG/DAT, no duplicate grid vs navigation, no download for root-only/unclassified entries; `TryGetRecord` rejects ambiguity/incompleteness. Newer RIO sidecar logic retained.
2. [#677](https://github.com/masarray/arsas/pull/677#issuecomment-6426842353) direct Actions EXE:
   - HEAD `46b90e9bc51d53008f7ccd1dc4ce0e9272fbc03d` **DIVERGES** from RC (`behind=4, ahead=1632`) — no false ancestry claim!
   - All four original paths present, 2 byte-identical regressions (`P0ActionsDirectPortableArtifactTests.cs`, `WindowsPackagingReproducibilityContractTests.cs`), build workflow and release docs evolved.
   - RC build.yml still includes canonical sealed Windows package plus exact single portable path `actions/upload-artifact@v7` / `archive: false` / `if-no-files-found: error`. This proves **configuration/source** exists, NOT that a current CI-run direct download was actually performed. Close obsolete independent duplicate only.
   - This is **not equivalent** to manual opt-in public FAT prerelease #660, which remains OPEN.
3. [#558](https://github.com/masarray/arsas/pull/558#issuecomment-6426894803) source-fenced COMTRADE selected-file UX:
   - HEAD `c2520cb1440a3e80ec3992a8bb8143ba063def2e`, RC ancestor `behind=0`.
   - All five paths present; byte-identical `Models/IedBrowserRecordProvenanceCache.cs`, `tests/ARSAS.Tests/M2IedBrowserFileSelectionAndProvenanceTests.cs`, `tests/ARSAS.Tests/WorkspaceNoiseP1RegressionTests.cs`.
   - RC still has source-scoped grid selection/owner/model and cancellation; download binds source+endpoint IED, verified manifest with SHA-256 digest, not filename/size inference. Evolved XAML/UI code checked, not byte-identity claimed.

All comments clearly state no extra CI run, no new physical COMTRADE/MMS/IED validation and no release/engine/main change.

## D18 findings — PRs that MUST remain open, not blindly treated as merged
- **[#653](https://github.com/masarray/arsas/pull/653)**: substantive CI-cost improvement **not present** at protected RC. Its proposal adds GitHub API docs-only PR classification (`pr-scope`, fail-closed on incomplete/error files) and optional special `ci-field-capture` label; neither the workflow markers nor its new tests are in RC. Historical PR HEAD `779bbdef9865004317e8fcf1de282bc0aacdd23f` **diverges** (`behind=5`). Keep OPEN; reconcile required checks and actual scenario proofs with [#542](https://github.com/masarray/arsas/issues/542) before transplanting any optimization. Optimize cost without weakening P0/safety workflows.
- **[#660](https://github.com/masarray/arsas/pull/660)**: 3/4 original file blobs equal RC (docs and two Python verifier/test scripts), BUT the critical manual publishing `.github/workflows/fat-direct-exe.yml` is **absent** from RC. The released #677 convenience path serves workflow artifact downloads; it does **not** create opt-in public GitHub FAT prerelease assets, human approval, explicit SHA/run qualification, or tag immutability. The retained doc also makes an outdated universal claim that Actions artifacts always require ZIP; current v7 archive:false code contradicts it. Keep OPEN for product/owner decision; do not install public publishing trigger or silently close as redundant.
- **[#479](https://github.com/masarray/arsas/pull/479)**: 3/4 historical files (`docs/audits/P7_6_FIELD_QUALIFICATION_INTAKE.md`, `scripts/test-p7-dual-ingress-diagnostic.py`, `scripts/verify-p7-dual-ingress-diagnostic.py`) MISSING from RC. `scl-interoperability-r7.yml` evolved. Head `175c400262d7b959bb73c045445314c08d8eaffb` DIVERGED (behind=1); no evidence of equivalent parser. Keep OPEN. This is diagnostic-consistency only, not independent real physical or PCAP proof.
- **[#594](https://github.com/masarray/arsas/pull/594)**: original `Models/RcbInspectionEvidence.cs` and historical tests both evolved. Current RC test `DiscoveryOnlyValuesNeverMasqueradeAsCurrentRcbState` with further explicit snapshot regression replaces historical `DiscoveryValuesAppearAsHistorical_NotAsCurrentReadOnlyCheck`. This affects current-vs-discovered RCB and operator trust; keep OPEN until semantics audited with owner.
- **[#599](https://github.com/masarray/arsas/pull/599)**: original `M214BrowserCollapsibleReportSectionsTests.cs` and layout evolved; original `AreExpandedByDefault` test no longer exists; now RCB flags collapse together with control block section. Do not assert exact operator default/expanded-state parity. Keep OPEN until WPF owner acceptance.
- **[#582](https://github.com/masarray/arsas/pull/582)**: 2 historical GOOSE/SCL test blobs exact, 6 evolved implementation files. Ownership/safety overlap with #606 and #753; KEEP OPEN for safety+physical gate.
- **[#572](https://github.com/masarray/arsas/pull/572)**: all five implementation/regression paths evolved, no byte-identical source; phasor/CF data-truth sensitive. No closure.
- #606/#615 and #753–#756 safety, #726 Write, #716 Boolean, #688 SG7 performance, #732 IP GUI are protected and untouched. #520 BCUGE BRCB discontinuity physical cause and #446 full interoperability require independent test; remain open.

## Exact RC CI reuse / no new runner minutes
- Windows Build [40165560413](https://github.com/masarray/arsas/actions/runs/40165560413) SUCCESS.
- SCL R7 [40165560386](https://github.com/masarray/arsas/actions/runs/40165560386) SUCCESS.
- Interoperability reference guard [40165560409](https://github.com/masarray/arsas/actions/runs/40165560409) SUCCESS.
These previously green exact-RC workflows do not certify real IED FAT, and some other workflows on that SHA were skipped or failed; do not say all checks passed. No CI triggered by source-free D18 closures.

## Live final paginated GitHub inventory
D17 **102** open PRs, 16 independent open issues, 56 historical PRs closed-unmerged. D18 **three** more closed: **99 open PRs**, **16** independent open issues, **59 historical PR closures**. Counts independently paginated after mutations (99 PR on page1, 0 on page2; independent issues 11+5).

## D19 engineering handoff
1. Put CI cost next: reconcile **#653** with latest RC on an isolated, auditable owner-approved branch. Verify missing file-list fail-closed, no Windows skips on mixed/source/security changes, exact-SHA canonical Windows proof, branch protection and downstream artifact dependency before merger. Reuse existing exact-SHA CI; no repeated build storms.
2. Validate actual direct EXE artifact download from a new, intentionally qualified RC build when a build is otherwise warranted, not just workflow YAML.
3. Decide product-policy ownership of #660: is a public FAT-only prerelease needed in addition to direct private Action EXE? If needed, harden manual publishing/approval and update stale doc; otherwise explicitly close as rejected/superseded **without asserting shipped functionality**.
4. Preserve #479 field diagnostic intake implementation as unique missing acceptance work, and #594/#599 and #582 for substantive operator/safety reviews rather than administrative closure.
5. Finish cross-thread coordination and hardware qualification before advancing #727/#729 out of draft. Avoid branch moves and engine pin changes without owner sign-off.

**D18 change types:** GitHub PR status/comments, this isolated Markdown checkpoint only; no C# or WPF engine runtime changes.
