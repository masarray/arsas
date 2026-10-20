# P0 Read / Command checkpoint — 2026-10-21

## Immutable ancestry (never resume this work from `main`)

- **Accepted newer UI/ZIV/GoCB line:** #650 `5a33fb101026707caf6410ff097a569cfabf3939`, inheriting field-accepted ZIV/GoCB #649 `4c232184f64584308396ea18ff6c82700ac02d29` and approved UI phases #646–#648. Full 11/11 exact-HEAD workflow PASS at the source checkpoint.
- **Independent field Browser/Report line:** #644 `80e4836affce0b1f5f142c2966199a2ef5af41b3` → #645 `2b4e4ea0b917b6c10fbe4b64b6a3a7b127870598`; source/CI green, original physical SV/OR/BL and all data model values **not proven**.
- **Current integration:** draft #652 stacked directly on #650. Exactly four authoritative files from #645 were replayed after verifying both pre-change files on #650 are blob-identical to original #642. Regressions and mainline UI/GoCB/Command Dock protections are retained.
- **Pinned engine:** ARIEC61850 #183 `c85d4e20db722bd0c2729f2533b323b13d3ef735`. The engine P0-C trial #184 is separate and **not pinned or consumed by #652**. CI-only draft #185 triggers Windows engine validation for #184 because the engine workflow omits the stacked base branch.
- **No main merge/public release** until the current *combined* app and engine exactly matched head passes Windows, package, and authorized physical FAT.

## P0-R production code

1. `Models/IedBrowserReadPlan.cs`: single source-of-truth exact MMS reference + FC admission; per-model weak compiled read-only metadata index and immutable `IedBrowserSchemaIndex` reused only for the same frozen `LiveDiscoveryModel`. Model replacement produces new index; duplicates remain quarantined and all observed values continue exclusively in `LiveValues`.
2. Explicit Read now states the *actual clicked scope* (IED, DO, LN, or other) and per-FC **scheduled** counts, not synthetic wire observations. Thus a 5-target DO Read is visibly distinct from a complete LN Read. Passive bounded focus still intentionally excludes SV/OR/BL, while verified explicit Read DO/LN/All admits them; CO is never a scalar Read.
3. `IedBrowserReadResultInboxTests` continues to enforce synchronous final inbox drain before the success subtitle; P0H tests preserve anti-spoof identity, epoch switch, FC count and duplicate rules. No new network call, timer or global mutable IED-state cache.

## P0-C source-proven delay and safe trial

Physical SIEBCU/CSWI4 samples show SBOw service 5–7ms + *post-response application-error observation* 399–404ms + Operate service 2–3ms. Engine `Iec61850ControlServiceOptions.ApplicationErrorGracePeriod` defaults to 400ms; `SelectWithValueCoreAsync` awaits it after successful SBOw and before Operate. **Do not simply set it to zero:** delayed asynchronous negative LastApplError must still prevent Operate. Engine draft #184 adds stage labels/configured grace to wire evidence and tests negative events arriving immediately or after 35ms. A qualified no-fixed-wait path needs event-correlated identity (association/object/ctlNum/origin) plus negative, late, collision, timeout and disconnect regressions and field acceptance; no such performance fix is claimed yet.

## Field limitations and required gates

- User's actual 57-row XCBR1 CSV: 41 observed; 15 without MMS attempt evidence; 1 failed scalar projection. Four `Pos.subEna/subVal/subQ/subID` SV leaves were **not attempted**, rather than server-rejected. Read actual XCBR1 LN/DO explicitly, record exact app+engine SHA, planned FCs, typed GVA responses, invoke/partial-failure evidence, per-IED ledger and UI. Independent IEDScout only confirms comparator values.
- `SumSwARs.units.multiplier` returned no scalar in application evidence without raw GVA; classify as unresolved read/projection, not a confirmed server refusal.
- Report anomalies: 52 received BRCB frames, 13 sequence irregularities, BufOvfl=1, EntryID absent. Never infer lost SOE or continuity completion without synchronized wire capture.
- User's endpoint `192.16.1.192` is also assigned to the Windows host's `KM_Test` adapter; do not label a same-host simulator observation as physical relay.
- Required: all 11 CI workflows, targeted+full positive/negative suites, exact engine lock, portable packaged smoke/manifest, actual Read SV/OR/BL and control stage evidence on authorized device, plus UI/DPI and installer qualification. No automatic command/RCB writes or cyclic MMS process polling.

See `AGENTS.md`, `docs/WORKSTREAM_COORDINATION.md` and Oct-20 Global Read & Command Strategy attachment for governing constraints.
