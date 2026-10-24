# ARSAS SMV Tab — accepted FAT checkpoint, 2026-10-24

Status: **operator-accepted SMV visual/engineering workflow, consolidated integration checkpoint**  
Scope: IEC 61850-9-2 Sampled Values passive monitor and its necessary shared WPF Inter typography.  
Decision: merge accepted SMV-only stacked work onto an isolated integration checkpoint; **do not imply that `main` or the independent SG7/SSOT/Write/Control workstreams are released**.

## Canonical provenance (single source of truth)

- Approved final code HEAD: `4b2b62f882a81988a39a2f82d1ac8ff67592be02` ([source PR #722](https://github.com/masarray/arsas/pull/722)).
- **Consolidated merge**: [PR #725](https://github.com/masarray/arsas/pull/725), merge commit `bcf0ceb92a4dc6081d725ce0bd0c8f60bc102cc4` on isolated integration checkpoint; merge comparison to accepted source changed **only this handoff file**.
- Integration PR exact-source-SHA CI: [Windows build #40052239441](https://github.com/masarray/arsas/actions/runs/40052239441), [SV validation #40052239421](https://github.com/masarray/arsas/actions/runs/40052239421), [P1 advisory #40052239471](https://github.com/masarray/arsas/actions/runs/40052239471), all SUCCESS.
- FAT candidate workflow: [Windows Build ARSAS 40050735060](https://github.com/masarray/arsas/actions/runs/40050735060): ARSAS managed regressions **2784/2784**, engine **24/24**, native/managed bridge **6/6**, portable EXE smoke success.
- [SV validation 40050735053](https://github.com/masarray/arsas/actions/runs/40050735053) passed; [CI P1 advisory 40050735051](https://github.com/masarray/arsas/actions/runs/40050735051) passed.
- Operator-tested FAT EXE: [artifact 12543442068](https://github.com/masarray/arsas/actions/runs/40050735060/artifacts/12543442068), `ARSAS-1.6.40-win-x64-portable.exe`.
- EXE SHA-256: `1bd10822cf9523ed42c30f7824e8a2e4f5e0eeb8089b9591ae72c2f0e53b44d8`.
- Operator acceptance: visual presentation declared **good and finished** in the GitHub handoff chat, requesting checkpoint/closure on 2026-10-24.
- Completed stacked SMV sequence: [#708](https://github.com/masarray/arsas/pull/708) → [#711](https://github.com/masarray/arsas/pull/711) → [#714](https://github.com/masarray/arsas/pull/714) → [#715](https://github.com/masarray/arsas/pull/715) → [#717](https://github.com/masarray/arsas/pull/717) → [#719](https://github.com/masarray/arsas/pull/719) → [#722](https://github.com/masarray/arsas/pull/722).
- SSOT parent anchor: `00afef5be5a5a4a84c071eb1a82435b8b3c262df` (head of draft #705). The checkpoint starts at precisely this SHA, **without advancing #705**.

## Locked behavior and regression invariants

1. Passive acquisition, bounded coherent stream snapshots, independent SV publisher identity, live stream discovery and synchronization provenance all remain truthful. `smpSynch` is *publisher-reported*, never verified PTP/GPS/GM lock.
2. Selecting SV1/SV2/SV3 must not blank the window or show spurious Sync Unknown between valid snapshots. Last coherent data are retained under explicit HOLD and **never attributed to the newly selected publisher**. Stream list and eight measurement rows retain presentation identity with change-only notifications.
3. For verified mapped 9-2LE, Voltage waveform first, Current below; voltage left and current right in Measurements; RMS, phasor magnitude+phase, Power P/Q₁/S/PF consistent with the selected coherent publisher, no naive cross-publisher pairing.
4. RAW counts are uncalibrated. RAW phasor callouts display channel+angle only and Power remains fully collapsed. Do not claim U/I calibration or fabricated signal frequency/timebase.
5. UI phase angle displayed 0–360° without altering signed mathematical phase/DFT calculations. Correct shared axis zero reference, per-phase stacked axes, ms timeline, cursor intersections and callouts.
6. SMV renders compact electrical information by default. Diagnostics (APPID, MAC, VLAN, gap, restart, scales, measurement methods, rate provenance) remain accessible by hover/Diagnostics; safely short, wrapped sync HOLD tooltip. Embedded Inter is the shared desktop typography.
7. Do not reintroduce panel recreation, all-row replacement, per-packet WPF invalidations or synchronous GUI work. No default animation timers and no unnecessary user dialogs.
8. Changes in this stack beyond SMV WPF components are intentional *shared font consistency* or strict source-test guards, not a license to modify Command/Write behavior.

## Cross-thread integration contract

- **SG7 performance / SSOT (#679, #695, #698, #703, #705):** no overwrite of reactive viewport/indexing, model journal, live signal priorities or performance counters. This checkpoint is based on SSOT #705; promote only through an independently reviewed integration that accounts for in-flight branch changes.
- **MMS Control / Command Dock / Write (#721 and active FAT branches):** none of those command/safety paths is changed by this checkpoint; keep their separate PR approvals and tests.
- **Future SMV changes:** branch from this integration checkpoint or a proven descendant, record previous HEAD, require exact-head Windows app+engine+native+SV validation and physical signal FAT. No post-acceptance formatting churn without a new issue.
- **Do not force merge into `main`.** As of audit, `main` and the SMV final source had diverged significantly (>1000 commits ahead and independent commits on main). The dedicated checkpoint is an **integration milestone, not a production release or main merge**.

## Reproduction / recovery

1. Fetch branch `checkpoint/smv-tab-v1640-fat-accepted-20261024`; canonical GitHub consolidated [PR #725](https://github.com/masarray/arsas/pull/725) merged at `bcf0ceb92a4dc6081d725ce0bd0c8f60bc102cc4`. This documentation-only follow-up does not change compiled code.
2. The reviewed FAT source tree is `4b2b62f882a81988a39a2f82d1ac8ff67592be02` and the above Windows artifact is the independently identified build; never relabel an older #719 or #717 artifact as this checkpoint.
3. Restore using the integration merge commit as a starting SHA, not a force-push to shared workstreams; revert through normal PR if regressions appear.
4. Component PRs #708/#711/#714/#715/#717/#719/#722 are **closed as consolidated into integration PR #725** (not individually merged into `main`). Historic divergent SMV PRs #667/#671/#675/#680 are **closed as superseded proposals**; their unique historic commits were NOT implicitly cherry-picked and source branches are retained. Any independently useful old change needs a fresh reviewed proposal.
