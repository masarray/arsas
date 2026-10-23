# CI-P2F — Field Capture canonical regression reuse

Issue #442.

## Decision

Field Capture may reuse canonical ARSAS full-regression evidence, but it may not
reuse the canonical portable executable.

On PR #441 both lanes used the same ARSAS head and integration engine SHA, yet
the executables were not byte-identical:

- canonical: 78,397,596 bytes, SHA-256 49ff985f9ab969483836810dc15895da5ea0b9b046cfb57fdb96b8897269e774;
- Field Capture: 78,397,601 bytes, SHA-256 ef80317a8665b7fa900473650864f8d7fb7f9250229880c11b5f4a558aaab40e.

Therefore this milestone optimizes regression execution only.

## Pull request path

The Field Capture workflow still:

- validates the physical baseline commit 4467124775d8d9d76f3db194f9fbfd97144767a8;
- resolves and checks out the current integration engine;
- validates P0-5c association-scoped Smart Discovery source contracts;
- executes deterministic P0-5d wire-proof PASS/FAIL fixtures;
- validates route/lifecycle markers before packaging;
- runs publish-windows-portable.ps1 independently;
- smoke-tests the resulting Field Capture executable;
- uploads the Field Capture package and field-specific evidence.

The duplicate explicit solution restore/build/dotnet-test sequence is removed.
Full ARSAS regression is instead required from the exact PR synthetic merge SHA,
exact integration engine SHA, canonical Build ARSAS artifact and all-pass TRX.

publish-windows-portable.ps1 still performs its own restore and dotnet publish;
therefore the Field Capture binary remains independently built in its own lane.

## Manual workflow_dispatch

Manual packaging cannot depend on a PR artifact. It runs the reusable CI-P1
exact-SHA full-regression job first. Field Capture packaging proceeds only when
that manual regression job succeeds.

## Evidence artifact

The existing ARSAS-smart-discovery-pr134-test-evidence artifact name remains.
For PR runs its regression component is now
ci-smart-capture-canonical-proof.json rather than a duplicated local TRX.
P0-5D/P0-5E evidence remains field-lane evidence.

## Static cost

Compared with P2E, workflow-source restore/build/test tokens fall from
12/12/14 to 11/11/13. setup-python rises from 16 to 17 for exact canonical
proof verification. Workflow files containing an explicit dotnet build/test
fall from 11/10 to 10/9.

## Rollback

Restore the explicit Field Capture restore/build/test sequence. Never substitute
the canonical EXE for the Field Capture EXE unless a later reproducible-build
milestone proves byte identity on matching source and engine SHAs.
