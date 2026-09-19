# P0-5g — Golden Discovery Production Promotion & Mainline Merge Readiness

P0-5g converts the field-only smart discovery lane into a controlled production promotion. It is deliberately fail-closed: a normal build does not activate the smart route until a reviewed physical P0-5f authority has been accepted and a P0-5g promotion authority has been generated.

## State machine

P0-5g has three observable readiness states:

1. `BLOCKED` — one or more physical/provenance/engine gates are missing or invalid;
2. `READY_TO_PROMOTE` — physical P0-5f authority exists, the engine PR head is green and evidence-compatible, no disallowed post-authority ARSAS source changes exist, and the tracked production switch is still false;
3. `READY_FOR_REVIEW` — a P0-5g promotion authority is present, bound to the physical authority and validated engine head, and the tracked production switch is true.

CI fixtures may test the state machine but can never create production authority.

## Fail-closed build routing

`evidence/SmartDiscoveryPromotion.props` owns the tracked production switch.

Before production authority:

```xml
<SmartDiscoveryProductionPromoted>false</SmartDiscoveryProductionPromoted>
```

`Directory.Build.targets` activates the smart route only when either:

- the build is the dedicated `Smart Discovery Field Capture Build` evidence workflow; or
- `SmartDiscoveryProductionPromoted=true` has been written by the P0-5g promotion authority writer.

This prevents merging the PR from silently converting every ordinary build to the field route before physical evidence is complete.

## Engine evidence-compatible ancestry

The physical discovery evidence baseline remains:

```text
4467124775d8d9d76f3db194f9fbfd97144767a8
```

A newer ARIEC61850 PR #134 head may be accepted for merge-readiness only when:

- it is a descendant of that baseline;
- its exact head CI is green;
- none of the tracked discovery-critical files listed in `smart-discovery-production-promotion-target.json` changed between the physical baseline and the new head.

Changes outside those paths, such as independent SCL export/test work, do not automatically invalidate the physical discovery request-budget evidence. Any change to a discovery-critical path requires a new physical evidence cycle rather than an exception.

## Physical P0-5f authority

The production readiness verifier expects a physical authority created by:

```text
scripts/new-smart-discovery-repeat-run-authority.ps1
```

That authority must have:

- `Phase=P0-5f-authority`;
- `Status=physical-finalized`;
- production evidence only;
- the same device identity and evidence engine baseline;
- the reviewed independent repeat-run set.

After the physical authority commit, only the narrow promotion-only allowlist in the P0-5g target may change before promotion. Runtime discovery source changes invalidate readiness.

## Verify readiness

The verifier requires local checkouts of ARSAS and the exact engine PR head:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-smart-discovery-production-readiness.ps1 `
  -TargetPath .\evidence\smart-discovery-production-promotion-target.json `
  -EngineLockPath .\engines\ARIEC61850.lock.json `
  -PromotionPropsPath .\evidence\SmartDiscoveryPromotion.props `
  -ArsasRepositoryPath . `
  -EngineRepositoryPath ..\ARIEC61850 `
  -ArsasHeadCommit <exact-arsas-head> `
  -EngineHeadCommit <exact-pr134-head> `
  -EngineHeadCiConclusion success `
  -PhysicalAuthorityPath .\evidence\smart-discovery-repeat-run.authority.json `
  -PromotionAuthorityPath .\evidence\smart-discovery-production-promotion-authority.json `
  -OutputJson .\P0-5G-production-readiness.json
```

Before physical evidence exists the expected result is `BLOCKED`; that is a safety result, not a reason to bypass the verifier.

## Create production promotion authority

Only a `READY_TO_PROMOTE` proof may be promoted:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\new-smart-discovery-production-promotion-authority.ps1 `
  -ReadinessJson .\P0-5G-production-readiness.json `
  -TargetPath .\evidence\smart-discovery-production-promotion-target.json `
  -PhysicalAuthorityPath .\evidence\smart-discovery-repeat-run.authority.json `
  -EngineLockPath .\engines\ARIEC61850.lock.json `
  -OutputAuthorityPath .\evidence\smart-discovery-production-promotion-authority.json `
  -OutputPropsPath .\evidence\SmartDiscoveryPromotion.props
```

The writer has no fixture or force bypass. It writes `SmartDiscoveryProductionPromoted=true` only together with a cryptographically bound P0-5g authority.

## Mainline ready-for-review gate

`READY_FOR_REVIEW` from the local promotion verifier is necessary but not sufficient to mark PR #324 ready. Before leaving Draft, also verify on the exact ARSAS head:

- Smart Discovery Field Capture Build = success;
- Smart Discovery Golden Budget Lock = success;
- Smart Discovery Golden Provenance = success;
- Smart Discovery Repeat-Run Stability = success;
- P0-5g Production Promotion Guard = success;
- generic ARSAS build/test workflow = success;
- relevant installer/evidence validation workflows = success;
- ARIEC61850 PR #134 exact head `.NET CI` = success;
- no unresolved blocking review threads;
- PR remains mergeable against current `main`.

Do not mark the PR ready, enable auto-merge, or merge either repository while any of these conditions are unresolved.

## Production source cleanup

The current promotion mechanism keeps the historically proven build-time route patcher but places it behind the fail-closed promotion switch. Removing the patcher and moving the equivalent calls directly into `NativeIec61850Client.cs` is a separate source-cleanup operation and must preserve binary/runtime behavior. Do not combine that cleanup with the physical-evidence promotion commit because it would invalidate the exact ARSAS commit lineage being promoted.
