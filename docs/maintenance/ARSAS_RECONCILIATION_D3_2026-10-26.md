# ARSAS — Reconciliation D3: M2.17-D source safety rescue and C264 archival closure
**Date:** 2026-10-26 (Asia/Jakarta, afternoon)  
**Previous:** [D2 verified checkpoint](ARSAS_RECONCILIATION_D2_2026-10-26.md). **This is a new file on the isolated maintenance branch**, not an overwrite of a parallel owner or a promotion to main.

## 1. Live state (paginated verified after D3)
| Metric | D3 entry | D3 final live state | Why |
|---|---:|---:|---|
| Open PRs | 130 | **129** | New unique safety draft #753 (+1), historical documents #502/#503 closed (-2) |
| Open issues excluding PRs | 17 | **17** | No issue closed |
| Published stable | v1.6.42 | v1.6.42 | Unmodified |
| Consumer main | `14e5dddfbaae012b61a021366c1a864d83d316f2` | same | No main mutation |
| Engine main | `3d21238d0b62f24faecd5948a4ee238c82b35966` | same | No engine mutation |
| Protected RC #727/#729 | `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` | same | Draft integration/physical gates remain |
| Historical unique risks | #606, #615 | remain OPEN | Do not close on patch creation/CI alone |

**Cumulative original baseline** 154 PR /19 issues → now **129 PR /17 issues**, with 26 historical PR closures and two issue closures offset by one new, narrowly justified corrective PR. Do not count #753 as a closed backlog item. No mainline or stable release merge.

## 2. P0 current-tree source rescue — draft PR #753

[Draft #753](https://github.com/masarray/arsas/pull/753) — `M2.17-D safety: reject incomplete unbound GOOSE and foreign-domain SGCB/GoCB reads`.

- Source branch: `safety/m217d-raw-goose-sgcb-admission-20261026`, head `d0fbb5c2ae9f69f56badbba9bef5e15d66a78dc7`.
- Base: `release/arsas-integrated-rc0-20261024` HEAD `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, comparison ahead **5**, behind **0**, exactly **5 changed files**.
- `Models/IedBrowserGooseInspection.cs`: `OrderedUnboundWireEntries` checks complete nonzero wire count equal to list, contiguous one-based Order/zero-based DataSetIndex, nonempty real received values and no `<missing in frame>`. It returns raw unbound entries, **never** guessed FCDA.
- `MainWindow.IedBrowser.cs`: existing unbound PDU display now requires this full-vector gate; otherwise suppress raw rows. No change to verified directory-bound member mapping or canonical MMS/Report SSOT.
- `Models/IedBrowserReadPlan.cs`: one exact `LogicalDevices.MmsDomain` HashSet prevents initial read-only SGCB and GoCB state reads from arbitrary foreign MMS domains. Existing verified inventory, bounded budget and no EditSG/CnfEdit/Operate rules remain.
- `tests/ARSAS.Tests/IedBrowserGooseInspectionTests.cs`: added ordered/mismatched, duplicate, missing, empty and source-callsite assertion cases.
- `tests/ARSAS.Tests/IedBrowserControlStateEvidenceTests.cs`: added mixed current-domain vs foreign SGCB/GoCB and all-foreign fail-closed cases.
- **Not a copy** of old PR #606: historic 7-test file contains removed `OrderedWireEntries` and obsolete navigation assertions, and PR is diverged. [#606 now links #753](https://github.com/masarray/arsas/pull/606#issuecomment-6409867406), remains OPEN until new code integrated and 7-method parity audit complete.
- A single new **draft** PR was opened only after all five branch commits, avoiding an Actions wave per intermediate commit; no redundant Windows build manually dispatched. Does not touch #726/716 MMS Write/Control, #615 late-session alias, #688 SG7, RC release docs, native bridge, or engine lock. **No merge yet**.

### Exact-head CI evidence at checkpoint
[Windows canonical build/test job](https://github.com/masarray/arsas/actions/runs/40280841713/job/121433555670) on exact #753 HEAD: verified source/engine lock, solution restore, **Build solution success**, pinned engine regression and **ARSAS application regression tests success**, exact-SHA test attestation success. Native bridge build/portable EXE packaging stage was still **IN PROGRESS** at last recheck; final CI result not asserted. [SCL package job](https://github.com/masarray/arsas/actions/runs/40280841715/job/121433555800) **success**; source classifier [run 40280841750](https://github.com/masarray/arsas/actions/runs/40280841750) passed (reusable build comparison skipped). CI progress does not establish field GOOSE/IEC physical qualification or authorize merging #729.

The audit environment has no local `.NET 8` SDK, so no claim of independently run local dotnet tests; exact managed test success is sourced only from GitHub Actions step evidence.

## 3. Historic C264 unique docs rescued — #502/#503 closed with archival evidence

[Sanitized C264 engineering archive](archive/C264_SAME_HOST_SANITIZED_ENGINEERING_EVIDENCE_2026-10-26.md) committed on independent audit branch at `d7e512a234a020ac5a841e44fee039b431f11bb5`, blob `260de207930dea76db9f77e052ae693012644a2d`.

All **three original historical docs** were read fully, traced individually to exact head/blob, distilled without public private network/capture identifiers, and retained on existing original source branches:
- **[#502](https://github.com/masarray/arsas/pull/502)** head `46639977ab104b9201dfdd65af0b92f37b06d627`: `C264_HANDOFF_PROMPT` blob `f71458a87150936a4446c091cb73c89f4b0c790e`, `C264_SAME_HOST_REPAIR_PLAN` blob `79095cfc289f9a0987756b140cd2caf0ed713f20`. [Individual pre-closure comment](https://github.com/masarray/arsas/pull/502#issuecomment-6409920170); closed **unmerged**.
- **[#503](https://github.com/masarray/arsas/pull/503)** head `4613838f608152a263b54adb84d07f85155a43cf`: `C264_SAME_HOST_FIELD_HANDOFF` blob `9a9ef262cc046b9548bd73821b5ece46527478d6`. [Individual pre-closure comment](https://github.com/masarray/arsas/pull/503#issuecomment-6409922605); closed **unmerged**.

Archive maintains historic Report/RCB count evidence (30 DataSets, 364 members, 207 selected points, 134 observed, 73 pending, 143 without q, 4/29 RCB ingress), exact 14 FCDA `stVal/q` pairs, independent MMS vs L2 GOOSE/Npcap identities, typed FCD versus rejected Report structure, C264 positive command chain *as historical provenance only*, P0–P5 plan, foreign-NIC/GUI centering/checkbox, Explorer 7-versus-6 header suspicion, and required physical gates. No claimed source integration or production-fix equivalence. Full original docs remain reachable for audit; no historical source deletion.

## 4. Open safety blockers, active owners & next actions

- **#606 (D):** draft #753 implements scoped present-tree guards but must **finish full canonical CI, review and integration** before #606 administrative disposition. Proof of actual parser behavior still requires physical/synthetic GOOSE frame tests, and WPF interface acceptance is separate.
- **#615 (D/E):** proven current alias path (MainWindow WPF-owned `SignalDefinition` → `Iec61850MonitorRuntime.ExecuteControlAsync` → `NativeIec61850Client` mutates `request.Signal` after awaits). Existing old snapshot blob `96277fceb3490a778ab0d7113fadc0bb13edee1a` may inform but **never blind cherry-pick**; coordinate with current active MMS Control/Write owner. Design command-only snapshot + association-fenced publication and targeted late-native regression before code movement.
- Protected **#729 / #727 / #726 / #716 / #732 / #688 / #374 / #435 / #546 / #543 / #649** remain OPEN. Main, engine, frozen v1.6.42, physical R7/R10 and native bridge pin unchanged.
- **#567/#570** vector/spectral semantic parity still unproven despite ancestry, do not mass-close. **#493/#495/#520/#525/#651** independently gated.

### Next focused batch
1. Verify exact #753 Windows package job outcome and targeted test methods, inspect any failure step if red; keep draft until full required check policy and owner review are met. Do not promote #729.
2. Audit ##606 remaining association-fenced GOOSE receive boundary against RC and the new tests. Once #753 is actually integrated (not merely green), re-evaluate closure of historical #606.
3. Coordinate #615 command-only snapshot isolation in **its existing active Control/Write lane**, not via a competing broad PR. Preserve operator Write/readback #726.
4. Continue bounded, per-file+semantic historical Browser/Report cohorts; require exact 5-field individual comment before any closure.
5. Preserve accepted stable release and physical qualification gates. No fan-out Actions reruns for bookkeeping.

**Engineering takeaway:** D3 is a real current-RC safety patch plus sanitized document rescue (two safe closures), not cosmetic backlog shrinkage. Final required CI/native bridge and field qualifications remain separate acceptance gates.
