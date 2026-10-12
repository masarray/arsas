# CI-P3B — Sealed canonical portable promotion into Field Capture

Issue #449.

## Basis

P3A proved that two independent Windows builders using the same ARSAS source,
ARIEC61850 engine and ArdIrec revision produced byte-identical native bridge and
portable executable outputs.

Accepted parity:

- synthetic merge: `3794b29fa71695ec4ea099469ae2e55054d3d3f8`;
- engine: `648124097621046f5f127ceb1cf853fea54db730`;
- ArdIrec: `7dbc7149db668126a493ce338264363a6ec5ec00`;
- bridge SHA-256: `fa8efcaf26e8ebb0a29808a2e52ad5c9f466ec2df146945d29c5d662987c41ce`;
- portable SHA-256: `9fa7fac00c96acb24c98e64ff91ca9ee1709d9f1b89bf55030284ee3da4fc369`;
- portable size: 78,789,758 bytes.

## Architecture

P3B uses the existing `ARSAS-windows-package-input` as the sole canonical
promotion substrate. Field Capture does not use a second ad-hoc artifact
download implementation.

Build ARSAS seals:

- the tested portable executable;
- its portable build-identity JSON;
- installer input;
- canonical all-pass TRX and manifest;
- native bridge bytes;
- prebuilt verification runtime and fixtures;
- SHA-256 and size for every promoted file.

The sealed package verifier independently checks exact source, engine, ArdIrec,
workflow run/attempt/event, full-regression counters, every file digest, the
portable build identity and Windows-safe archive paths before materialization.

## Pull-request Field Capture path

Field Capture still owns and executes:

- exact candidate checkout;
- physical baseline engine evidence;
- P0-5c Smart Discovery source invariants;
- deterministic P0-5d wire-proof PASS/FAIL fixtures;
- integration-engine inspection;
- Smart Discovery route/lifecycle verification;
- portable runtime smoke;
- field artifact publication.

The duplicate PR native bridge + self-contained .NET publish is removed.
After sealed package verification, the exact canonical portable executable and
its exact build identity are copied into the Field Capture dist directory,
smoke-tested again and uploaded under the existing
`ARSAS-smart-discovery-pr134-win-x64` contract.

## Manual workflow_dispatch

Manual Field Capture remains independent. It runs the reusable exact-SHA full
regression and then performs its own portable publish. This prevents a manual
field build from depending on a historical PR package.

## Readiness and fail-closed behavior

P3B inherits the shared sealed-package verifier's bounded artifact readiness
policy. A transient GitHub archive HTTP 404 may be retried only within the
existing deadline. Persistent 404 times out fail-closed; non-404 request
failures remain hard failures. Completed failed Build ARSAS runs are never
reused.

## Authority boundary

P3B changes only the normal PR Field Capture packaging source. It does not
change installer authority, release promotion, physical field evidence, or IEC
61850 runtime behavior.

## Rollback

Restore the PR Field Capture independent publish path. Do not add a separate
portable downloader or weaken the shared sealed-package identity/hash checks.
