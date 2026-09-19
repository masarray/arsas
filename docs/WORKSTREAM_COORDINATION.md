# ARSAS Workstream Coordination

This file is a durable coordination note for parallel development threads working in the same ARSAS repository. It exists to prevent a later workstream from accidentally replacing already accepted work from another branch.

## Current integration baseline

COMTRADE P1D.7 was field-accepted and integrated to `main` by PR #300 at commit:

`c3e2ac3ef6e6a3a49f47c5155a0ba4a7c00bcb6b`

The ArdIrec native bridge dependency used by that workstation was integrated first through ArdIrec PR #41. ARSAS intentionally keeps the exact field-qualified ArdIrec revision pinned in `engines/ARDIREC.lock.json` until a separately validated dependency update is performed.

The production `AGENTS.md` already present on `main` remains authoritative.

## IEC 61850 qualified runtime baseline

The accepted IEC 61850 runtime line was qualified on ARSAS PR #324. Its final
source head is `b0f25569a398c7bbdc3a3a34409dbd74ebf49444`, its merged commit is
`2b8e8adfd019bd087dc338dcc32bffefec53370f`, and both commits resolve to the
same Git tree `cccb50607161f8637e597b43d71038ffd4becfdc`.

The corresponding portable artifact was produced by workflow run
`35412542175` with SHA-256
`6805b878f91f3d526cc50f3e81e3eb9a1f72500217dacc8921e50bdc349c5fcc`.
`.release/qualified-source.json` is the machine-readable authority for this
lineage. The Windows release workflow must verify its release-sensitive
fingerprint before packaging or publication.

Public wording and identifier neutralization may be layered on this baseline
only when the exact combined head passes regression and packaging gates. A
version bump, documentation update, or merged/closed PR status does not by
itself establish a new qualified runtime authority.

## Parallel FAT workstream

The FAT workstream remains independent and is currently represented by the stacked FAT branches/PRs, including PR #290, PR #296, and PR #303. Their existing field gate remains authoritative; this coordination note does not waive or replace it.

Before any FAT branch is merged to `main`, the FAT thread/agent MUST integrate the latest `main` containing COMTRADE P1D.7, resolve conflicts intentionally, and rerun its exact-head CI and field acceptance on the combined codebase.

Do not merge a stale FAT branch directly over `main` merely because its earlier CI was green.

## No-regression boundary when FAT integrates main

The combined branch must preserve the accepted COMTRADE workstation behavior already on `main`, including at minimum:

- synchronized Time Signals C1/C2 cursor identity between waveform and upper ruler;
- synchronous lightweight cursor movement without full waveform rebuilds;
- one P cursor for Phasor and one H cursor for Harmonics;
- stale-result rejection / bounded latest-wins analysis and readout scheduling;
- analog-before-digital deterministic track ordering;
- Clear/Auto and manual signal-selection authority;
- true RMS and Primary/Secondary presentation behavior;
- native ArdIrec Phasor, Harmonics, cursor measurements and six-loop Locus integration;
- first-click Fault Records / FILE reliability changes;
- accented/legacy COMTRADE CFG display-name handling;
- retained/screen-space waveform rendering and extrema-preserving LOD;
- bridge-only ArdIrec packaging and installer/runtime smoke behavior.

If a FAT conflict touches any of these areas, keep the current `main` behavior unless there is an explicit newer accepted requirement and a new regression test proving the replacement.

## FAT-specific behavior must also remain intact

Integrating `main` into FAT must not weaken the FAT workstream's own accepted architecture or evidence rules. In particular, preserve the existing Engineering/FAT authority model, lifecycle ownership, static DataSet behavior, evidence integrity, multi-IED isolation, defensive telemetry semantics, virtualization, bounded shutdown, and field-gated merge policy described by the active FAT PRs.

## Required integration procedure for the FAT thread

1. Start from the latest FAT candidate that passed its own current field/CI gate.
2. Merge or rebase the latest `main` into that candidate; do not overwrite `main` files wholesale from the FAT branch.
3. Resolve conflicts by authority, not by choosing one side mechanically. `main` is authoritative for already-landed COMTRADE/runtime contracts; the FAT branch remains authoritative for FAT-specific changes not already superseded on `main`.
4. Run the full application regression suite plus FAT-specific CI on the exact combined head.
5. Re-run any COMTRADE/integration checks triggered by files touched during conflict resolution.
6. Repeat the FAT physical field gate on the exact combined candidate before final merge.
7. Only after all required gates are green should the FAT stack be collapsed/merged into `main`.

## Stacked PR hygiene

When a top-level PR already contains the complete accepted history of lower stacked PRs, land only the top-level integrated change and close obsolete lower PRs as superseded rather than merging the same history repeatedly.

This is the same approach used for COMTRADE: PR #300 was integrated as the consolidated result, while obsolete lower P1D PRs were closed as superseded.

## Rule for all future parallel threads

Before final merge, every thread must verify the current `main` head and account for work that landed from other active threads after its own branch was created. A previously green branch is not automatically safe to merge after `main` has moved.

The integration target is always: **preserve all accepted behavior from both workstreams, then qualify the exact combined head.**
