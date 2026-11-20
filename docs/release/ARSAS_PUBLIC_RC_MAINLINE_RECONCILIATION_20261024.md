# ARSAS v1.6.42 public RC — mainline reconciliation ledger
Date: 2026-10-24 Asia/Jakarta

## Deterministic source ancestry
- Public stable main at audit: `45348eccf24e941dd185e5fb729217923846c610`; last published v1.6.41 source `e581a26dea5545034378adbeed5b78fc80aab773`.
- Accepted integrated RC parent: `1f1a471508e477df7e44abb0c21916b771d88024`; source already passed Build #40071354642 (2957/2957), R7 #40071354635 (1272/1272 engine), SV #40071354652 and P1 #40071354650.
- This *staging-only* reconciliation commit has two explicit parents: accepted RC first and public main second; its tree preserves RC operator features, while selectively bringing main-only production release and public stable records forward.
- Canonical candidate version is **1.6.42** to avoid reusing published v1.6.40/v1.6.41 identities; initial release authority is **prerelease only**, subject to current exact-source CI and user field acceptance. The stable release request `.release/windows.json` and the last-published `.release/published.json` deliberately continue to describe public v1.6.41. **Do not change `.release/windows.json` for v1.6.42 before stable promotion approval.**

## Main-only changes intentionally preserved
- `.github/workflows/release-windows.yml`: real Ubuntu bash/POSIX release-ready fix and Windows manual release path.
- `.github/workflows/validate-io-testing.yml` + `.github/workflows/validate-sv-evidence.yml` + `scripts/test-ci-workflows.py`: domain-safe owner scoped triggers/fixtures, preventing both redundant SV waiters and missed IO FAT ownership.
- `.github/workflows/sync-release-documentation.yml`, `scripts/sync-landing-release.py`, `scripts/validate-product-source.py`: public site/release-sync and duplicate-title guards.
- `.release/published.json`, `.release/windows.json`, `landing/latest.json`, `landing/release-notes.json`, `landing/site.json`, `docs/WINDOWS_RELEASES.md`, `evidence/v1.6.41-operator-release-acceptance.json`, `scripts/test-exact-operator-release-acceptance.ps1`: last **published stable** provenance retained intact.

## Main-only changes NOT reintroduced as old UI regressions
- Historical seven-tab shell and deleted IED Browser, obsolete RCB-only exporter/selection workflows, earlier SOE inspector and stale navigation geometry.
- Current RC eight-tab Browser, final cool-blue SMV, Smart IP 410x348, reports/SSOT, protected Write/Control, Command Dock sorting, v2 responsiveness and recent field-authorized scope must remain.
- The old v1.6.41 exact-scoped release exception in main's R10 policy/workflow is not evidence for the new engine `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`; no universal automatic physical acceptance is asserted.
- Other overlapping main application files require explicit UI/RCB/SOE compatibility verification; never blindly select old main implementations.

## Mandatory prerelease gates
1. Current combined HEAD (with main as second parent) must pass complete Windows Build, pinned engine native, R7, SV/IO domain tests and portable smoke. Verify exact candidate source tree and SHA256 of the generated EXE; the old v1.6.40 artifact is not this candidate.
2. COMTRADE and Reference Guard manual independent workflows still require checked proof on exact candidate if not auto triggered. No retargeting to main, force-pushing or bypassing failed gates.
3. Operator visually accepts latest IED Browser fixed order Reports, DataSets, Data Model, Files, Setting Groups, GOOSE; selected IED name/IP bold; Smart IP compact dialog, final SMV, controls.
4. Three-IED combined physical FAT (SG7, ZIV AVR, GR_X), reconnect, reporting and SOE/BufOvfl continuity, typed Write/readback and WPF latency must be signed off before claiming stable/mainline qualification.
5. Public **prerelease** must be labeled `v1.6.42` *prerelease*, with explicit limitations and artifacts bound to a single signed/checksummed SHA. DO NOT publish default-latest stable; the current v1.6.41 remains latest stable until verified qualification and separate signoff.

## Known current risks
- Pinned ARIEC PR #192 is still draft and not main stable; user-reported field observations on prior checkpoints do not certify the new merged binary.
- SG7 SOE/EntryID and four BufOvfl/continuity findings require classification and field capture.
- Standalone independent workflows that do not auto-run may need manual Actions workflow_dispatch selection of this exact branch.
