# CI-P3B — Canonical portable promotion into Field Capture

Issue #449.

## Basis

P3A proved that Build ARSAS and Smart Discovery Field Capture produced
byte-identical ArdIrec bridge and portable executable outputs from matching
source, ARIEC61850 and ArdIrec revisions.

## Pull-request path

Field Capture still owns its field-specific P0-5c source checks, P0-5d wire
fixtures, physical baseline checks, integration-engine inspection and portable
runtime smoke. It no longer republishes the same portable executable.

After exact canonical full-regression proof resolves the Build ARSAS run ID,
Field Capture waits boundedly for that run's portable artifact and validates:

- repository/run/workflow source identity;
- exact synthetic merge SHA;
- exact ARIEC61850 SHA;
- exact pinned ArdIrec lock SHA;
- portable SHA-256 and byte size against the build identity;
- valid native bridge hash/size evidence;
- deterministic managed build and reproducible native-link flags.

Only then is the canonical EXE copied into the Field Capture dist directory,
smoke-tested again in the Field Capture lane and uploaded under the existing
field artifact name.

## Manual dispatch

workflow_dispatch has no PR canonical run dependency. It preserves the reusable
exact-SHA full regression followed by an independent Field Capture publish.

## Authority boundary

P3B changes only the PR Field Capture packaging source. Installer validation,
release publication, physical evidence and IEC 61850 runtime authority are
unchanged.

## Efficiency

The normal PR path removes one complete native bridge + self-contained .NET
portable publish. The workflow source still contains setup-dotnet/publish for
manual fallback, so static token-count budgets do not pretend this runtime
saving is a source-topology reduction.
