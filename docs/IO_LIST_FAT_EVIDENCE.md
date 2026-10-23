# IO List FAT Evidence Testing

ARSAS includes a dedicated IO List FAT workspace for repeatable IEC 61850 digital-indication testing. The workflow starts from an approved ARSAS Excel template, limits the active view to the imported IED and signals, records ordered state-transition evidence, and exports a resumable project together with reviewable Excel and native PDF reports.

> Availability: this document describes the current `main` development line. Check the stable release notes before assuming the latest published installer contains this workflow.

## Purpose

The workspace is designed for FAT teams that need to prove that an imported digital indication:

1. has a trustworthy OFF baseline;
2. changes from OFF to ON;
3. returns from ON to OFF;
4. preserves the IED timestamp when supplied;
5. records the independent ARSAS capture timestamp;
6. keeps quality and acquisition source attached to the transition;
7. produces a traceable result without editing the approved source workbook in place.

The first release is intentionally limited to imported **SDI / digital indication** rows. Analog tolerance testing, output commands, protection-function injection, GOOSE campaign execution, Sampled Values campaigns, formal approval signatures, and multi-user review are separate scopes.

## Start paths

The first-launch workspace presents two distinct choices:

- **General IEC 61850 Testing** — add an IED by IP address for model discovery, live monitoring, reporting, files, GOOSE, SCL, diagnostics, and guarded control.
- **FAT / IO List Testing** — import an ARSAS IO List workbook or open a portable `.arsas` project.

The IO List path does not replace the general engineering workspace. It adds a controlled test-plan layer on top of the existing live IEC 61850 runtime.

## Approved workbook contract

ARSAS reads the `ARSAS_SIGNAL_IMPORT` worksheet and validates required identity and state columns before creating a project. Important fields include:

- `ProjectId`
- `SchemaVersion`
- `TestPointId`
- `TestEnabled`
- `ImportReady`
- `BindingStatus`
- `IEDName`
- `IPAddress`
- `SignalName`
- `DataType`
- `FC`
- `ObjectReference`
- `ExpectedONRaw`
- `ExpectedONText`
- `ExpectedOFFRaw`
- `ExpectedOFFText`

Rows that are not SDI are skipped by the first release. Missing required headers, inconsistent project identity, duplicate or unsafe test-point identity, invalid expected states, and other blocking findings reject the import instead of being guessed.

## Live binding

After import, ARSAS binds each planned test point to the live-discovered IEC 61850 signal for the selected IED. The workspace keeps the imported identity separate from runtime evidence and shows whether a point is ready for testing.

A test session is scoped to one IED at a time. All enabled and safely bound imported points for that IED may be observed together. The IO List workflow is read-only toward the IED; it does not create DataSets, write RCB configuration, or operate controls.

## Transition state machine

A point passes only after a new ordered transition sequence has been observed:

```text
Trustworthy OFF baseline
          ↓
OFF → ON transition
          ↓
ON evidence captured
          ↓
ON → OFF transition
          ↓
OFF evidence captured
          ↓
PASS
```

Important rules:

- An initial ON image is never accepted as ON evidence. ARSAS waits for a new OFF baseline first.
- Duplicate or out-of-order observations do not create evidence.
- The first image after reconnect is a baseline, not a transition.
- A connection-generation change after ON evidence forces review because OFF continuity can no longer be proven.
- Good or valid IEC 61850 quality can be accepted automatically.
- Invalid, bad, failed, or blocked quality is rejected.
- Missing, unknown, or questionable quality produces review evidence rather than an automatic pass.
- PASS requires accepted ON and OFF evidence captured in order.

Ordering is based on the ARSAS observation sequence and capture time. An IED timestamp is preserved as device evidence but is not trusted as the sole ordering authority.

## Evidence captured for each transition

Each ON or OFF transition can retain:

- transition type;
- previous and current normalized digital state;
- raw observed value;
- IED timestamp, when supplied;
- ARSAS capture timestamp;
- IEC 61850 quality;
- acquisition source such as BRCB, URCB, or polling;
- monotonic observation sequence;
- connection generation;
- evidence verdict and reason.

The UI presents current value, quality, source, ON evidence, OFF evidence, and final state for the selected IED.

## Durable project state

ARSAS stores local progress under the current Windows user profile and autosaves changes to a project snapshot. Evidence journals are append-only JSON Lines files with a hash chain. Completed points remain completed after reopen; interrupted ON-only continuity is restored as review rather than silently resumed as pass-eligible evidence.

Resetting or repeating a test creates a new attempt in the runtime workflow instead of rewriting historical evidence as if the first attempt never existed.

## Export Excel

**Export Excel** creates a copy of the approved source workbook and writes result fields back to matching `TestPointId` rows in `ARSAS_SIGNAL_IMPORT`.

The source workbook is never modified in place. The result copy can include:

- ON and OFF observed value;
- IED timestamp;
- ARSAS capture timestamp;
- quality;
- acquisition source;
- ON and OFF verdict;
- overall result;
- authoritative state-machine notes.

ARSAS refuses partial result output when workbook rows and project test points do not match exactly. The file is written through an atomic temporary-file replacement.

## Native PDF report

**Export PDF** creates an A4 landscape PDF directly in ARSAS. The report does not depend on a browser, HTML conversion, printer driver, external executable, or third-party PDF layout package.

The native PDF engine is ported from the project-owned ARIEC60870 implementation and uses application-specific PDF 1.4 primitives:

- document catalog and pages tree;
- built-in Helvetica, Helvetica-Bold, and Courier Type 1 fonts;
- vector lines, rectangles, and rounded cards;
- wrapped text and paged tables;
- repeated page and IED headers;
- cross-reference table, trailer, and metadata.

The report contains project identity, workbook SHA-256, project counters, per-IED sections, expected ON/OFF states, IED and ARSAS timestamps, quality, acquisition source, final result, and reason.

The engine is intentionally bounded. It does not claim PDF/A, embedded custom fonts, digital signatures, accessibility tagging, or formal document approval.

## Portable `.arsas` project

**Export `.arsas`** creates one portable project that can be opened from **FAT / IO List Testing → Open ARSAS Project** on another workstation.

A current IO FAT `.arsas` project contains:

```text
manifest.json
project.snapshot.json
source/
  approved-workbook.xlsx
evidence/
  *.evidence.jsonl
report/
  IO-FAT-Report.pdf
  IO-FAT-Results.xlsx
README.txt
```

The package preserves:

- the approved source workbook;
- current project and test-point state;
- sealed evidence journals;
- the native PDF report;
- the Excel result workbook;
- integrity metadata.

The legacy `.arsas-iofat` filename remains readable for backward compatibility, but new exports use `.arsas`.

## Integrity and import safety

ARSAS validates portable projects before restore. Checks include:

- supported package and snapshot version;
- expected `io-fat` package kind for new projects;
- project identity consistency;
- SHA-256 of the snapshot, source workbook, PDF report, and Excel report when present;
- evidence-journal SHA-256 and hash-chain verification;
- bounded entry count and file sizes;
- safe archive paths without traversal;
- exact source-workbook identity.

New report hashes are additive. Legacy packages without native report hashes remain readable within the older package contract.

Export is blocked while an IED FAT session is active so the evidence journal can be stopped, sealed, and verified first.

## Cross-laptop continuation

A typical continuation flow is:

```text
Laptop A
Import workbook → bind IED → run FAT → stop session → Export .arsas

Laptop B
Open ARSAS Project → connect/discover intended IED → obtain new live baseline
→ continue only unfinished enabled points
```

Completed points are excluded from the next active session by default. Restored live values are not treated as current until the new workstation receives trustworthy runtime observations.

## Engineering boundaries

ARSAS supports the evidence workflow but does not replace:

- an approved FAT procedure;
- calibrated injection or process simulation equipment;
- protection-function and trip-path testing;
- drawing, setting, logic, and cybersecurity review;
- plant or switching authority;
- independent witness and formal acceptance;
- vendor responsibility;
- IEC 61850 conformance certification.

Use only authorized endpoints and synthetic or approved project workbooks. Do not publish customer signal lists, IED addresses, credentials, confidential SCL, relay settings, or evidence packages without authorization and sanitization.

## Related documents

- [Project README](../README.md)
- [Architecture](ARCHITECTURE.md)
- [Validation checklist](VALIDATION_CHECKLIST.md)
- [Product roadmap](../ROADMAP.md)
- [Support and evidence sanitization](../SUPPORT.md)
