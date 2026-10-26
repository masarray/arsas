# ARSAS D20 — actual workflow qualification, CI-P4 draft PR, and D21 handoff
**Date:** 2026-10-27 Asia/Jakarta. Continues [D19](ARSAS_RECONCILIATION_D19_2026-10-27.md).

## Milestone actually delivered
- Feature branch `ci/p4-failclosed-optimized-gates-d19-20261027` advanced from `6065c6e8e36d67d1f5488153aa99a01183c3cf0d` to `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`. Added **one actual workflow-expression regression test** to existing `scripts/test-ci-workflows.py`. No extra GitHub workflow or runner introduced.
- New test extracts the exact checked-out YAML `github-script` and `build-windows` / `build-smart-capture` expressions, evaluates the classifier against mocked GitHub paginated file-list API, and evaluates job expressions via Node.js VM. **26 cases:** 12 changed-file/API cases, 7 Windows admission cases, 7 Field Capture label/manual cases. Tested against a separately constructed local workflow fixture before push. On GitHub CI the complete workflow guard Python suite finished SUCCESS, confirming test inclusion and execution on a real Ubuntu runner.
- Opened **one, single-wave DRAFT** [PR #757](https://github.com/masarray/arsas/pull/757), HEAD `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`, BASE `release/arsas-integrated-rc0-20261024` at exact `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`. This PR is **not** targeting main; it has not been merged. Avoid further pushes/reruns to preserve CI budget.

## Real GitHub Actions results observed (commit e0c9724, run group 40407726xxx)
- **SUCCESS** [CI P0 Workflow Integrity run 40407726690](https://github.com/masarray/arsas/actions/runs/40407726690), job 121869711900. Logs: existing `scripts/test-ci-workflows.py` reports `Ran 12 tests ... OK`; all other CI P0 workflow guard suites and structural budget audit pass. Node-based dynamically extracted D20 test is included in that 12-test suite.
- **SUCCESS** [Build ARSAS preflight job on run 40407726657](https://github.com/masarray/arsas/actions/runs/40407726657), job 121869711894: logs `Changed paths=4, Windows required=true`. This is evidence the **real GitHub PR event** correctly admitted Windows for workflow/test changes.
- **SKIPPED by design** [Smart Discovery Field Capture run 40407726714](https://github.com/masarray/arsas/actions/runs/40407726714): both manual exact-SHA regression and optional heavy `build-smart-capture` jobs skipped on draft/unlabeled #757. This proves new opt-in gate for this scenario, not labeled/manual physical paths.
- **SUCCESS** [CI P1 Advisory Classifier run 40407726721](https://github.com/masarray/arsas/actions/runs/40407726721).
- **SKIPPED** Validate ARSAS Windows installer run 40407726656.
- **STILL IN PROGRESS at handoff observation**, do not report as green: [Build ARSAS run 40407726657](https://github.com/masarray/arsas/actions/runs/40407726657) canonical Windows job; [COMTRADE integration run 40407726689](https://github.com/masarray/arsas/actions/runs/40407726689), [IO List Testing run 40407726707](https://github.com/masarray/arsas/actions/runs/40407726707), [SV evidence run 40407726693](https://github.com/masarray/arsas/actions/runs/40407726693).
- Total **8 workflows** triggered in the single PR wave. This reveals remaining broad GitHub workflow fan-out (#542); D20 did not create another wave or rerun green statuses.
- No real docs-only PR event, labeled Field Capture event, manual dispatch, field IED FAT or billed-minute savings measurement has yet been performed. Do not say fully qualified or savings achieved. GitHub App still returns HTTP 403 when trying to inspect main branch protection/required checks.

## Live repository invariants
- ARSAS main remains `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966` as last verified D19. Published stable v1.6.42 unchanged.
- Protected RC #727/#729 stays draft at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- Safety #753–#756, Write #726/#716, SG7 perf #688, IP GUI #732 isolated/untouched; no C#/XAML/engine lock changes.
- Inventory after #757 open: **100 open PR**, **16 independent open issues**, **59 historical closed unmerged** (D15–D18). #653 old historical PR and #542 CI cost issue both remain OPEN with [D20 coordination notes](https://github.com/masarray/arsas/pull/653#issuecomment-6428209861).

## D21 handoff
1. **Do not push another commit to #757 to seek a different CI outcome until examining all current jobs.** Read exact-SHA workflows via GitHub connector (run IDs above), especially canonical Windows, COMTRADE, IO and SV. Investigate failed job logs first and only surgically fix verified defects; do not rerun successes.
2. D20 has actually verified CI P0 and normal code-impacting PR Windows admission; still need explicit owner approval for required-check/branch-protection behavior, real docs-only skip and real labeled/manual Field Capture event. A skipped Windows job cannot produce immutable sealed package; preserve downstream fail-closed evidence checks.
3. #542 remains open; the initial 8-run wave indicates future opportunities in specialized COMTRADE/IO/SV workflow trigger/verification reuse. Never remove physical/safety proof just to reduce runners.
4. If/when all relevant checks green and owner accepts policies, review D20 PR #757 for controlled merge into the RC **only**, not main/stable release. No implicit merge permission, and no release/engine repin or physical IED acceptance inference.
5. Maintain unchanged safety owner #606/#615/#753–#756, Write #726/#716, SG7 #688 and GUI #732. Continue evidence-only backlog cleanup from D18/D19 with no mass closures.

**D20 accomplishments:** concrete regression implementation, draft RC-only PR #757, live successful P0 check and one real skip gate, and issue handoff. **Outstanding:** canonical Windows/specialist jobs completion, owner required-check verification and field/data-path qualification.
