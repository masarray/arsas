# ARSAS FAT/SAT Test & Evidence Workspace

The FAT/SAT workspace is a bounded execution and evidence workflow. It does not turn an operator-entered result into formal IEC 61850 conformance evidence by itself.

## Workspace contract

- File format: `*.arsas-fat.json`
- Current schema: `1`
- Workspace, test-case, and evidence identities are stable GUIDs.
- Save uses a temporary file and atomic replacement.
- Unsupported future schema versions are rejected instead of guessed or silently migrated.
- A workspace is limited to 1,000 test cases and 100 evidence files per test case.
- One evidence file is limited to 256 MB.

## Test-case outcome

Supported outcomes are:

- `NotRun`
- `Pass`
- `Fail`
- `Review`
- `Blocked`
- `NotApplicable`

A package is complete only when no test remains `NotRun` and no test is `Fail`, `Review`, or `Blocked`. The operator remains responsible for the result and its supporting evidence.

## Evidence integrity

When a file is attached, ARSAS records:

- display name;
- absolute source path in the local working document;
- SHA-256;
- byte length;
- media type;
- attachment UTC time;
- optional description.

Before audit-package export, ARSAS reads every source file again. Export is rejected when the file is missing, its size changed, or its SHA-256 no longer matches.

## Audit package

The ZIP package contains:

- `workspace.json` — portable workspace with source paths rewritten to package-relative evidence paths;
- `report.md` — human-readable scope, summary, disposition, test outcomes, deviations, and evidence references;
- `evidence/...` — immutable attached evidence files;
- `SHA256SUMS.txt` — SHA-256 for every package entry except the checksum file itself.

The final ZIP also receives a package-level SHA-256 shown to the operator.

## Provenance

The workspace records:

- ARSAS version and informational build identity;
- ARIEC61850 repository, ref, and immutable commit;
- project, site, bay/system, IED, operator, witness, and scope.

## Acceptance boundary

The workspace supports repeatable FAT/SAT execution and traceable evidence. Formal conformance, universal interoperability, calibrated measurement, and authorization for live process control require separate evidence and governance.
