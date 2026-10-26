# ARSAS D15 — Routine Command Dock Refresh same-socket model epoch safety

**Date:** 2026-10-26 (Asia/Jakarta). **Continue from** [D14](ARSAS_RECONCILIATION_D14_2026-10-26.md), [D13](ARSAS_RECONCILIATION_D13_2026-10-26.md), D12–D1. D15 is NOT a stable release, protected RC merge, physical IEC 61850 IED FAT or source branch overwrite.

## Live preflight and concurrent-owner isolation

- At D15 entry 115 open PR / 17 independent issues, 42 historical PR closures; after **one new substantive stacked safety PR #756**, 116 open PR / 17 independent issues (pulls paginated 100+16; issues paginated 100+33, excluding PR-shaped issues).
- ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2`, engine main `3d21238d0b62f24faecd5948a4ee238c82b35966`, protected #727/#729 integrated RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`; engine trial pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, accepted stable v1.6.42 unchanged.
- [#753](https://github.com/masarray/arsas/pull/753) raw GOOSE safety head `0652f66bed0c72dded20ff547e23e2ef1989341c`, [#754](https://github.com/masarray/arsas/pull/754) detached Operate head `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`; [#755](https://github.com/masarray/arsas/pull/755) combined command read-only inspection/post-Write model epoch draft head `7d235554225aa0341561b4e887a852fb6fd8e269`, exact-head Windows/SCL [40325732039](https://github.com/masarray/arsas/actions/runs/40325732039) and [40325732197](https://github.com/masarray/arsas/actions/runs/40325732197) **SUCCESS**. D15 found this exact HEAD already advanced by parallel thread and **never overwrote #755**.
- Freeze owner lanes [#726](https://github.com/masarray/arsas/pull/726) Write/readback `42c7cfe1364faaeeee64180bd82e5dbef4f7355a`, [#716](https://github.com/masarray/arsas/pull/716) Boolean `f53857d5f66d55d80b0f4d04eec91df5fc48053d`, [#688](https://github.com/masarray/arsas/pull/688) SG7 `774321313aac96ec87a4f24edf61731d032237e6`, [#732](https://github.com/masarray/arsas/pull/732) IP form `c47a622037110a09ed1e16653aac0c44b5f40de2`, accepted SMV/RC unaffected.

## Root cause: routine Refresh bypassed model epoch gate

In the tested #755 `MainWindow.xaml.cs:RefreshControlValuesAsync`, normal user Refresh obtains selected `SignalDefinition` rows and calls `_runtime.InspectControlAsync(deviceId, signal, cancellation)` **without** the optional `stillCurrentPublicationTarget` callback. The #755 facade owns nonreusable MMS association GUID and exact ObjectReference checks, but a live rescan/rebinding may exchange `device.Signals` and/or `device.LiveValues.Association` while retaining the same MMS socket/GUID. In that case old results or errors from read-only inspection could be published into the wrong Command Dock epoch. This is an operator-facing state correctness risk, **not evidence of a physical misoperate or a native Operate defect**.

D13 already implements a matching model epoch predicate for `ReinspectConfiguredControlAsync` after ctlModel Write. D15 reuses its exact proven predicate **instead of adding another concurrent ledger, queue, or network call**.

## Real D15 engineering: ONE stacked two-file PR and deterministic tests

**New** [draft #756](https://github.com/masarray/arsas/pull/756), branch `safety/d15-refresh-inspection-model-epoch-20261026`, exact source SHA [`098ec4abdf7ce8681ec97111f12f3f6151eade78`](https://github.com/masarray/arsas/commit/098ec4abdf7ce8681ec97111f12f3f6151eade78). Created directly from **unchanged #755 HEAD** `7d235554225aa0341561b4e887a852fb6fd8e269`, base PR targeting **`integration/d11-goose-command-safety-rehearsal-20261026`**, exactly **1 commit ahead, 0 behind, 2 changed paths**, no automatic merge:
1. `MainWindow.xaml.cs`—capture MMS `_runtime.GetControlAssociationId(device.DeviceId)` and model `device.LiveValues.Association` once before UI await in `RefreshControlValuesAsync`; fail closed if no association. New local `IsCurrentRefreshDevice` delegates to shared `Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget` to require exact GUID, epoch, connected state. `IsCurrentRefreshTarget` additionally requires `device.Signals.Contains(candidate)` instance identity, not just matching IEC path strings. Check after yield/preflight; pass predicate into existing facade which enforces it **before native dispatch, before typed GUI publication and before catch fault revocation**. Check before local success/fault text and suppress stale batch projection/status.
2. `tests/ARSAS.Tests/M217P0RefreshControlModelEpochTests.cs`—new deterministic tests with valid current GUID/epoch/object and rejected: same MMS GUID but re-bound signal instance, mismatched model epoch, new MMS GUID and offline. Source-flow contract test checks predicate passed to facade, no extra native call, UI success/fault/projection guards in correct order. Existing #755 managed/native regressions retained.

The patch does NOT change `NativeIec61850Client`, ARIEC61850 engine, Write/readback, SBO/Operate wire semantics, GOOSE/SGCB or RCB, and adds **zero** new MMS reads or buffers.

### Exact-head CI (update with final results)
At first source checkpoint: [SCL R7 run 40331840419](https://github.com/masarray/arsas/actions/runs/40331840419) and [Windows Build run 40331840977](https://github.com/masarray/arsas/actions/runs/40331840977) **in progress**, reference guard [40331840933](https://github.com/masarray/arsas/actions/runs/40331840933) and P1 advisory [40331840873](https://github.com/masarray/arsas/actions/runs/40331840873) **SUCCESS**. SCL has passed application build, canonical regressions, and engine tests before portable packaging; Windows still processing build. Do not attribute D13 full CI to a different D15 head; no manual reruns. Recheck final Windows native bridge CTest/managed packaging, EXE publish/smoke and exact SHA artifacts.

## D15 final exact-head CI and user-test candidate (post-checkpoint)

**Full CI SUCCESS at EXACT stacked #756 HEAD `098ec4abdf7ce8681ec97111f12f3f6151eade78`**, still 2-file draft, parent #755 unchanged:
- [Windows run 40331840977](https://github.com/masarray/arsas/actions/runs/40331840977): **COMPLETED SUCCESS**. App Build, pinned engine canonical test, ARSAS managed regression (including two new model-epoch fence tests), exact SHA attestation, pinned ArdIrec native CTest, managed packaged native bridge integration, portable EXE publish, portable smoke, sealed package and artifact upload all succeeded.
- [SCL R7 run 40331840419](https://github.com/masarray/arsas/actions/runs/40331840419): **COMPLETED SUCCESS** on the same SHA, including canonical-export regression, engine build/tests, R7 portable publish and smoke.
- [Reference Guard 40331840933](https://github.com/masarray/arsas/actions/runs/40331840933) and [P1 Advisory 40331840873](https://github.com/masarray/arsas/actions/runs/40331840873) **SUCCESS**.
- Exact-head portable [artifact #12654339747](https://github.com/masarray/arsas/actions/runs/40331840977/artifacts/12654339747), CI name `ARSAS-1.6.42-win-x64-portable.exe` but this **is a D15 engineering build, not published stable v1.6.42**. [Test evidence #12653769824](https://github.com/masarray/arsas/actions/runs/40331840977/artifacts/12653769824), [native CTest evidence #12654109515](https://github.com/masarray/arsas/actions/runs/40331840977/artifacts/12654109515).
- **Safety owner gate remains OPEN**: review #756 against parent #755, real Direct/SBOw late inspection and GOOSE/SGCB cases on physical IED, #615 owner, #606 owner and protected RC #727. No auto merge, no live Operate authorization, R7/R10 physical FAT still required.

## Historic RCB UI audit #599 — KEEP OPEN

[Individual #599 source evidence](https://github.com/masarray/arsas/pull/599#issuecomment-6417376768): ancestor head `70ad34392fd9da0e6851f2995f740e8fd8f80aed` → RC ahead1267/behind0. All four original path blob IDs checked, original 3 test methods→two identical, 18 assertions→15 identical.
- `MainWindow.xaml` `3085e636d264eda299684423b91eaff65ef33ae0`→RC `ee28dfe80e011740f72d114522f4311f8d852879`
- `Models/IedBrowserDetailSectionLayout.cs` `09b64c38069a59fb874aa59101250e6e0d14f289`→RC `a14d6847c201d62084e7b729a95d13784600ca9f`
- `Models/IedBrowserPresentation.cs` `65ee75f118505f4d9aa7fb3d29bc4234dcbe2856`→RC `1b40ab6d53f7056944be507cefebc156054c2207`
- `tests/ARSAS.Tests/M214BrowserCollapsibleReportSectionsTests.cs` `6ff2aa8afda2d0dbf594911cdf7652f797ad8e0e`→RC `513a585cd370b1c9e6439aaa296f4eccf57f065d`. Historical both RCB sections expanded by default, only two Control children. Current Canonical last Report section default **collapsed** with Trigger Options grouped under Control Block (4 children), explicitly documented as user-requested default. Original live children preserved by collapse/expand; not equal UX behavior. **KEEP OPEN pending GUI owner sign-off**, never revert canonical solely to close old PR. Separate operational RCB provenance #594 remains open.

## D16 integration gate

1. Verify exact D15 #756 SHA `098ec4ab...` full CI, inspect failures if any, minimally correct only own branch with CAS/non-force. Keep per-SHA status precise.
2. Coordinate #756 stacked dependent on current #755; request owner review of routine refresh epoch/row identity, and use one canonical integration plan, **never merge parent and child indiscriminately**. #615 direct/SBOw and #606 7-case GOOSE/SGCB still require physical IED acceptance, and #727 RC release owner approval.
3. Continue other historical PR audit only with file-by-file tests and user-intent/semantic acceptance, no mass closure. Preserve accepted v1.6.42 and engine pinned trial SHA.
