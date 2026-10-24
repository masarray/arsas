# HANDOFF ARSAS — Integrasi Semua Thread → RC0 → Release Candidate
**Tanggal:** 2026-10-24 (Asia/Jakarta)  
**Status:** RC0 STAGING, **BELUM RELEASE**, belum lolos full exact-SHA CI maupun FAT fisik sebagai gabungan final.  
**Mandat thread baru:** tuntaskan konsolidasi dan verifikasi; jangan membuat ulang roadmap/fitur yang sudah ada, jangan mengorbankan SSOT/performa atau GUI operator.

## 0 — Mulai dari titik ini, bukan commit lama
- Repository: https://github.com/masarray/arsas
- **Satu integration lane RC:** `release/arsas-integrated-rc0-20261024`
- **RC0 coding head sebelum dokumen:** `6cc3aa1ab7aa6ea7cb1f2f580f00becca7a51255`
- Source baseline SSOT + responsive reporting: `integration/arsas-ssot-performance-p0-20261024` @ `b1d177885ff8cba6515e19f0aec33d1fc887519b` (PR #724 merged into lane ini).
- Field checkpoint immutable: `checkpoint/arsas-ssot-performance-field-accepted-20261024` @ `b136b927636219e03c9ce092311f673c46201645`; original field app `55ee72205d77d138fcf8f49f82cc688313c21af7`.
- Write/FAT authoritative candidates: PR #679 @ `04350e15f9be9d429cf1a908b81bab922c36f093` and PR #726 @ `42c7cfe1364faaeeee64180bd82e5dbef4f7355a`; both are OPEN, #726 draft.
- `main` at audit: `45348eccf24e941dd185e5fb729217923846c610`; 1533 commits behind the chosen integration stack, 17 ahead. **JANGAN langsung merge/force-push ke main**.
- Pinned ARIEC61850 engine in preserved stack: `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`. Do not repin without fresh exact pin CI + qualification.

## 1 — Pekerjaan integrasi yang SUDAH benar-benar dilakukan
1. **PR #724 CLOSED/MERGED** into isolated `integration/arsas-ssot-performance-p0-20261024`; shared Static DataSet SSOT, bounded WPF selection batches, existing IED-card green reporting line, no cyclic process polling; exact code head `fb4932d127807fa83bf3b1fb70058fbdfdb3c966`. Pre-merge full CI Build+R7 passed, but it is NOT proof of exact RC0.
2. Command Dock tap-first WPF sorting patch PR #721 `fb7ae16f07a0715924a88a8bd33245e8765b1353` is **already ancestor of RC0**. PR #721 is still OPEN for administrative clean-up. Do not merge a duplicate patch.
3. SSOT v2 phases #695, #698, #703, #705 and associated WPF viewport work are present by ancestry in the accepted stack; do not reapply stale PRs individually.
4. **RC0 source integration already staged:** exactly 10 Write/post-Write canonical SSOT files copied from PR #726; these ten blob SHAs match PR #679 byte-for-byte. All other files inherited from accepted `b1d177885f`. The RC0 code tree SHA `d7560aee6e4119e79c3242cdc3a7b1984a3080b9` equals PR #726 code tree SHA. No blanket merge of #679/#726 required.
5. DO NOT close #679/#726 merely because the code tree is staged. Close/supersede only after RC exact-SHA CI + mandatory owner signoff and link merge provenance. Keep historic PRs for audit.
6. Do not touch unrelated SMV screen/IED Browser ordering during reconciliation.

## 2 — Protected UI/functional invariants (release BLOCKERS on regression)
- **IED Browser navigation operator order:** Reports → DataSets → Data Model → Files → GOOSE → Setting Groups. Preserve visibility, navigation, data fidelity, and read/RCB subviews. Do not let sort changes reorder this fixed navigation sequence.
- **Advanced SMV tab:** cool-blue/white measurement design, compact professional layout, samples/graph/connection controls, no accidental palette/spacing rollback. RC0 preserved `Views/SmvInlineViewer.xaml` blob `7deb737cb58812fa35ad2fcae5cc2c4b876be82f`. Run ``SmvMeasurementCoolBlueCheckpointTests` and `SmvMeasurementPaletteRegressionTests` plus actual screenshot comparison.
- **Shared layout:** `MainWindow.xaml` blob from accepted baseline `d7bd312246d42fb7aa59f962207d8bb17e48b794`; Command Dock matching model/WPF display priority (`Services/IecNaturalReferenceOrder.cs` blob `335b83713651377f8c4a954db1b757c0b29f513c`). TapChg/BSC operable at top, Status Only (ctlModel=0) bottom; never convert TapChg INT8 0/1 into Boolean True/False.
- **Smart IP and IED card** appearance, saved endpoint behavior, NTP compact header, thin progress line green only during reporting setup, status/Stop responsive. Preserve Smart IP regression tests (`PremiumSmartIpWizardTests`).
- **MMS safety:** typed same-DO status+ctlVal, exact live ctlModel, fail-closed verified Select/Operate and association fencing. No inferred command authority from LN names, no fabricated values, no automatic breaker control.
- **Canonical SSOT:** Report/Read + quality/timestamp/source + association into same authority before Explorer, Browser, Global Live render. Preserve exact post-Write MMS Read/SSOT ingress; never display a stale optimistic Write value as readback.
- **Performance:** no cyclic MMS process polling while Static DataSet report-only active; WPF UI must stay clickable during discovery/selection, bounded/cancellable UI batches and single-flight; no DataGrid Reset storms, no uncontrolled background tasks; per-stage `P0_STATIC_UI_WORK` instrumentation must survive.

## 3 — Measured FAT baseline: do not extrapolate to RC0
From user DIAG 2026-10-24: SG7_BCU 77/77 displayed, 4/4 exact routed RCBs, 156/156 represented static members, no cyclic process polling; ZIVAVR 8/8, 1/1 RCB; GR_X 13/13, 2/2 RCB. Control evidence includes SG7 SBOw→Operate→positive termination→process feedback and ZIV TapChg Raise, integer readback. Earlier SG7 continuity anomalies remain **13 findings with four BufOvfl**. Discovery SG7 ~15.57s (directory ~5.20s, types ~3.68s, model ~1.51s, projection ~5.12s). User reports GUI is improving; controlled WPF input p95/p99 and reconnect/SOE parity are NOT certified. Do not call these 100%.

## 4 — Mandatory next-thread execution ORDER (no rebuilding old work)
**A. Repo/merge reconciliation (before any code):**
- Fetch live heads of RC branch, PR #679, #721, #724, #726, SMV branch and `main`. Abort/reconcile if any SHA differs from this snapshot; never force-push or reset.
- Verify RC0 tree code matches PR #726 as documented; diff RC0 against parent `b1d177885f` must be only the 10 Write/postwrite files plus this handoff document.
- Audit all other actively edited PRs touching `MainWindow.xaml`, `Views/SmvInlineViewer.xaml`, IED Browser navigation, Report/SSOT runtime, engine lock, Smart IP form. Do NOT bulk-merge 100+ historical draft PRs. Classify `already-ancestor` / `equivalent cherry-pick` / `needs conflict resolution` / `intentionally excluded`.
- Assess `main`'s independent 17 commits before promoting any RC. Preserve the UI and security changes from both sides.

**B. CI on ONE EXACT SHA from RC branch:**
- Create ONE draft promotion PR against the accepted integration lane (not `main`); execute **Build ARSAS Windows, R7 interoperability, native bridge, P0/P1, Reference Guard, SV, IO List, COMTRADE**; if a required workflow does not run, explicitly dispatch or call the documented PR workflow—do not treat absence as green.
- Add targeted UI+SSOT regressions: fixed IED Browser operator section order, SMV palette/layout, Smart IP, WPF tap-first/Status Only, BSC numeric 0/1, command safety, Report/Read canonical SSOT, exact post-Write readback, bounded selection progress green + Stop preemption, SG7 large-catalog static membership/RCB.
- Every FAIL must be fixed without disabling/changing tests to a weaker acceptance condition. Publish Windows portable EXE and smoke it from that exact commit. Record artifact run+SHA; do not point to older EXE.

**C. Physical FAT on resulting exact RC binary:**
- SG7: 77/77 initial image, 4/4 RCB ingress after startup AND reconnect, DataRef/FC/q/t/source and SOE/EntryID/buffer-overflow chronology. Triage 13 anomalies, distinguish startup sequence resets from actual lost SOE. Pending never disguised as zero/Good.
- ZIV: 8/8, 1/1, BSC Raise/Lower signed integer display and exact readback, Status Only last. Confirm operable command only under operator-approved test.
- GR_X: 13/13, 2/2, verified DPC controls/status-only distinction.
- All IEDs: repeat IED Browser / Explorer / Global Live parity, post-Write fresh readback; reconnect and cancellation/Stop. No unauthorized breaker Operate.
- UI: 3 IED simultaneous monitoring, SG7 large model, Static DataSet startup and scan freeze reproduction. Measure UI input/render p50/p95/p99 and `P0_STATIC_UI_WORK` inventory/authority/selection/total against field baseline; ensure progress green while arming and disappears cleanly.
- Visually compare 6 Browser sections (exact order), SMV beautiful blue/white tab, Smart IP, Command Dock alignment and BSC integer.

**D. Promotion/cleanup ONLY after B+C accepted:**
- Label status as `RC-QUALIFIED` only on exact source SHA + CI + FAT. Then request operator approval to promote into mainline. Don't bypass existing R10 Mainline Readiness / physical requirement.
- Close duplicate #721 as `superseded by inherited integration` only after verifying exact ancestry; close/supersede #679/#726 only with code-tree + merge evidence and owner approval, no duplicate merges. Do not mass-close unrelated historical issues.
- GitHub Release `prerelease` with single Windows portable EXE, SHA256, provenance, changelog, limitations; stable release remains blocked until R10 user acceptance.

## 5 — Required handoff acceptance report per milestone
Report: **branch + exact commit SHA**, changed files and behavior, test counts per exact Windows CI run, every failure, signed-off source+binary provenance, known regressions and next action. Milestones:
M0 source+ancestry audit; M1 SSOT+Write integration; M2 GUI protected baselines; M3 full exact CI+portable; M4 three-IED physical FAT+WPF p95/p99; M5 mainline/release+PR cleanup.
**Current truth:** M0/M1 preliminary candidate staged; M2–M5 NOT accepted. RC0 is a source candidate, NOT a releasable RC binary.

## 6 — Paste-ready command for next ChatGPT thread
"Open GitHub repository masarray/arsas. Read this entire file `docs/handoff/HANDOFF_ARSAS_ALL_THREADS_TO_RC_2026-10-24.md` from `release/arsas-integrated-rc0-20261024` and verify its SHA against live GitHub. Continue REAL coding/integration and CI on this SINGLE RC lane, not another roadmap. Baseline `b1d177885f`, staged Write union `6cc3aa1ab7`; preserve all SMV/IED Browser/Smart IP visual invariants and exact SSOT+MMS control gates. Do not merge into main or close active PRs before code CI+3-IED FAT; report SHA, exact CI, issues, and next action each milestone. Prepare a verified Windows portable Release Candidate, then reconcile old PRs safely."

