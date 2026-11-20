# ARSAS 1.6.43-rc.1 — isolated Windows field-test candidate

**Scope:** trial-only ARSAS P8/P9 SG7_BCU integrated reporting and model-driven UI, with independently verified background stable updater. This is NOT the public stable release, not proof of lossless buffered SOE, and not authority to issue live breaker controls.

## Installation safety
- This candidate must be built by CI from its **exact immutable Git SHA**, and its installer artifact must include `ci-installer-authority.json` and `SHA256SUMS.txt` with all-pass Windows installed-runtime smoke.
- The installer uses **dedicated AppId, ARSAS-RC folder, shortcuts and AppUserModelId**. It must never replace stable `ARSAS` or the SMV Preview product.
- Keep v1.6.42 stable installed as fallback. Use a nonproduction Windows workstation or VM and ensure no plant control commands are enabled.
- Prerelease rc.1 is excluded from `.release/windows.json`; do NOT update `landing/latest.json`, stable GitHub Release assets, or promote this branch onto main.
- Before installing, compare the downloaded EXE's SHA-256 with the checksum artifact. Windows SmartScreen can warn because the public binaries are not signed; inspect provenance before execution.

## Manual FAT (report observations, not unproved pass)
1. Verify installed `ARSAS RC` shortcuts, `ARSAS-RC` Program Files path, version, no conflict with stable/uninstall.
2. Connect first to authorized IEC 61850 test simulator. Confirm IEC 61850 identity, 77/77 SG7_BCU initial values, 4 independent BRCBs, live report mutations, and 0 process polling.
3. Monitor buffered reports and export diagnostics **including BufOvfl, SqNum, EntryID, TimeOfEntry, GI/Integrity reason**. Correlate resets to IED/server and network traces; do not infer event continuity from 77/77 displayed values.
4. Test deliberate disconnect/reconnect and IED ownership conflicts only in an isolated simulation environment. Verify bounded UI startup and accurate final state.
5. Use **read-only** model browser and evidence export for initial lab trials; do not perform physical Select/Operate or blind retry. Engine #172 control-queue qualification remains open.
6. Simulate a future *stable* manifest only in an isolated local test harness; production updater must continue using the official stable manifest and never automatically overwrite RC, terminate live IEC 61850 work or install without consent.
7. Confirm RC uninstall removes its own files and the public stable ARSAS remains launchable with no state loss.

## Promotion gates
- Canonical Build ARSAS, sealed package, Windows installer/install/uninstall smoke, regression tests and RC-specific installer identity test must be green for the **same commit**.
- P9 interoperability R7 and buffered SOE field gates may remain FAIL for an explicitly labelled lab-only RC, but MUST be resolved before any stable publication.
- If the release gate is not green, publish no public stable release; distribute only a GitHub Actions RC artifact with the exact SHA and limitations.
