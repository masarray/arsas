# P0-5f — Physical Golden Lock Finalization & Repeat-Run Stability Proof

P0-5f proves that the accepted same-IED discovery is not a one-off lucky run. It requires a production P0-5e golden lock plus at least three fresh, independent MMS associations made by the exact same ARSAS/engine artifact.

## Production prerequisites

The production path requires:

1. a P0-5e golden lock created from an independently reverified physical PCAP (`RawCaptureReverified=true`);
2. the exact field artifact and its `SMART-CAPTURE-BUILD.txt` manifest;
3. the tracked `smart-discovery-golden-target.json` and `smart-discovery-repeat-run-target.json`;
4. at least three fresh discovery runs, each starting from a new MMS association generation;
5. for every run: raw PCAP/PCAPNG, P0-5d PASS proof, and the local `P0-5F-*.json` runtime evidence emitted by ARSAS after the fresh association owner publishes authority.

Cached rediscovery on the same association is not a repeat run and intentionally does not emit new P0-5f runtime evidence.

## Per-run procedure

For each run, disconnect/reconnect so ARSAS creates a fresh association generation. Start capture before TCP/ACSE/MMS association establishment, perform one smart discovery, then stop capture only after discovery has completed.

Create the P0-5d wire proof:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-smart-discovery-pcap.ps1 `
  -PcapPath .\run-01.pcapng `
  -OutputJson .\P0-5D-run-01-proof.json
```

Locate the matching ARSAS runtime evidence under:

```text
%LOCALAPPDATA%\ARSAS\SmartDiscoveryEvidence\P0-5F-*.json
```

Then create a P0-5f run bundle:

```powershell
powershell -ExecutionPolicy Bypass -File .\new-smart-discovery-repeat-run-bundle.ps1 `
  -GoldenLockPath .\smart-discovery-golden-budget.lock.json `
  -ProofJson .\P0-5D-run-01-proof.json `
  -CapturePath .\run-01.pcapng `
  -RuntimeEvidenceJson .\P0-5F-run-01-runtime.json `
  -BuildManifestPath .\SMART-CAPTURE-BUILD.txt `
  -TargetPath .\smart-discovery-golden-target.json `
  -DeviceIdentity AA1E1F06R4 `
  -ArsasCommit <exact-40-char-arsas-sha> `
  -EngineCommit 4467124775d8d9d76f3db194f9fbfd97144767a8 `
  -OutputPath .\P0-5F-run-01-bundle.json
```

Production bundle creation independently re-decodes the raw PCAP, re-applies the P0-5e golden request budget, verifies exact build/target hashes, and requires engine KPI `TotalRequests` to equal the PCAP confirmed-request count.

Repeat this for at least three independently established associations.

## Finalize repeat-run stability

```powershell
powershell -ExecutionPolicy Bypass -File .\finalize-smart-discovery-repeat-run-stability.ps1 `
  -GoldenLockPath .\smart-discovery-golden-budget.lock.json `
  -RepeatTargetPath .\smart-discovery-repeat-run-target.json `
  -RunBundlePaths .\P0-5F-run-01-bundle.json,.\P0-5F-run-02-bundle.json,.\P0-5F-run-03-bundle.json `
  -OutputPath .\P0-5F-physical-finalization.json
```

A production PASS requires all runs to be bound to the same golden lock, device, ARSAS commit, engine commit, build manifest hash, and semantic-target hash. Every raw capture hash, runtime-evidence hash, and association generation must be unique.

Across all runs the following must be exactly stable:

- confirmed MMS request count;
- MMS service mix;
- engine smart-discovery deterministic signature;
- canonical directory model signature;
- ARSAS signal-projection signature;
- hierarchy type-probe budget;
- discovered model counts.

Every run must also preserve:

- zero semantic duplicate requests;
- zero duplicate GetNameList/GVA;
- no second naming sweep;
- engine `DuplicateRequests=0`;
- complete engine wire accounting;
- PCAP request count equal to engine KPI request count;
- peak outstanding no higher than the P0-5e golden maximum;
- same-IED semantic counts: 32 LD, 119 LN, 4,925 semantic points, 2 DataSets, and 34 runtime RCB instances before semantic family collapse.

The finalization output records the consensus request/service budget, all deterministic signatures, association generations, peak-outstanding range, bundle hashes, capture hashes, runtime-evidence hashes, golden-lock hash, and repeat-target hash.

## Promote reviewed physical finalization into authority

After the finalization JSON is reviewed and reports `Verdict=PASS`, create the immutable physical authority file:

```powershell
powershell -ExecutionPolicy Bypass -File .\new-smart-discovery-repeat-run-authority.ps1 `
  -GoldenLockPath .\smart-discovery-golden-budget.lock.json `
  -RepeatTargetPath .\smart-discovery-repeat-run-target.json `
  -FinalizationJson .\P0-5F-physical-finalization.json `
  -RunBundlePaths .\P0-5F-run-01-bundle.json,.\P0-5F-run-02-bundle.json,.\P0-5F-run-03-bundle.json `
  -OutputPath .\P0-5F-physical-authority.lock.json
```

The authority gate has no fixture override. It requires a P0-5e golden lock with `RawCaptureReverified=true`, a schema-v2 P0-5f PASS, and the exact run-bundle hashes referenced by the reviewed finalization. Every supplied run bundle must have `FixtureEvidence=false`; capture hashes, runtime-evidence hashes, and association generations must all be unique.

The resulting authority file records the exact ARSAS/engine commits, golden-lock hash, repeat-target hash, finalization hash, consensus signatures/budgets, run-bundle hashes, raw-capture hashes, runtime-evidence hashes, and association generations.

## Evidence authority

`-AllowFixtureEvidence` exists only for CI regression fixtures in bundle/finalization testing. Never use it for physical acceptance. The final authority writer intentionally exposes no fixture bypass.

P0-5f is physically complete only when both the production finalization JSON reports `Verdict=PASS` from at least three fresh physical associations and `P0-5F-physical-authority.lock.json` is generated successfully. Until then, `smart-discovery-repeat-run-target.json` remains in `awaiting-physical-golden-lock-and-three-independent-runs` state and `FinalizationAuthority` remains null.
