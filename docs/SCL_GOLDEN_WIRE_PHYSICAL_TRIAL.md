# Trusted SCL golden-wire physical trial

This document freezes the physical qualification contract for the ARSAS 1.6.36 trusted-SCL reporting lane after ARIEC reporting/bootstrap convergence.

## Immutable engine authority

- Canonical ARSAS field-trial base: `trial/scl-golden-wire-v1636`
- Qualification branch: `integration/ariec-convergence-0023ef9-v1636`
- ARIEC61850 convergence engine: `0023ef9a4373855497464ed3979e359c4041c95d`
- ARIEC source PR: `#132`
- ARIEC exact-head .NET CI: `#603` PASS
- Field-proven reporting/control baseline retained by the engine lock: `11ab2304482600c19ba979f4fc9021ddb46b9af9`

The engine lock is the build-time authority. Do not substitute another ARIEC checkout while collecting qualification evidence.

## Gate 1 — read-only safe trial

Run the exact portable candidate with Wireshark capturing TCP port 102.

```powershell
ARSAS-1.6.36-win-x64-portable.exe --scl-safe-trial "C:\path\IED.cid" "IEDNAME" "AP1" "192.168.x.x" 102
```

Expected network work is limited to association, Domain/VMD reconciliation and bounded sequential initial FC-root Reads. The safe-trial process exits immediately afterwards. It must not enter full signal discovery, DataSet-directory discovery, writes, control, RCB enable/GI, or dynamic DataSet services.

If interoperability of a multi-variable Read is uncertain, repeat on a fresh association with exactly one variable reference per Read:

```powershell
ARSAS-1.6.36-win-x64-portable.exe --scl-safe-trial-single "C:\path\IED.cid" "IEDNAME" "AP1" "192.168.x.x" 102
```

Keep both the generated JSON evidence and the corresponding PCAP/PCAPNG.

## Gate 2 — normal Play and trusted static reporting

Only after Gate 1 association/read behavior is understood, open the same verified SCL source and use normal Play.

Trusted-SCL Play verifies the imported source SHA-256 before socket activity, keeps the SCL IED/AccessPoint association identity, performs Domain/VMD validation and bounded initial Reads, and does not silently fall back to cached association or full discovery.

For Static DataSet report-only mode, ordered DataSet membership and RCB identity remain SCL-authoritative in memory. The trusted path does not perform a network DataSet-directory browse and does not create/delete a dynamic DataSet.

The InformationReport receiver must be registered before any report-control write. The expected startup sequence is:

- BRCB: whole-RCB Read -> `RptEna=true` -> whole-RCB Read -> whole-RCB Read -> one explicit `GI=true`.
- URCB: whole-RCB Read -> `Resv=true` when exposed -> `RptEna=true` -> whole-RCB Read -> whole-RCB Read -> one explicit `GI=true`.
- BRCB `ResvTms` is retry-only after a real direct-`RptEna` rejection; it is not the primary startup path.
- `GI=true` is a one-shot startup bootstrap only and is sent only after report routing is registered and activation/readback succeeds.
- GI rejection is a startup failure: unregister the monitor, disable `RptEna`, release any reservation touched by this client, and report failure instead of presenting an active monitor with unknown initial values.
- No network DataSet-directory browse, dynamic DataSet mutation, cyclic GI, or cyclic MMS process polling is allowed on the trusted-SCL report path.

For the AA1E1F06R4 qualification target used by the golden comparison, expected evidence is:

- BRCB family `Buffer`: a concrete live indexed instance is enabled without pre-reserving it; startup GI is accepted; Digital report data arrives.
- URCB family `Unbuffer`: a concrete live indexed instance is reserved when `Resv` is exposed, enabled, startup GI is accepted; Analog report data arrives.
- All 58 selected static DataSet members receive an initial value without waiting for a process change.
- Structured members such as total power factor remain schema/semantic projected rather than silently falling back to an unrelated scalar.

## Gate 3 — steady state and cleanup

After the startup initial image:

- values must continue from InformationReport traffic/event updates;
- no periodic MMS process polling or repeated GI may be introduced;
- buffered backlog is applied in receive order so the canonical current-state plane retains the latest supplied value per signal while quality/timestamp-only updates do not erase the previous primary value;
- Stop/Close must disable every report enabled by this client;
- URCB reservation must be released when this client touched it;
- BRCB reservation must be released only when the compatibility fallback actually touched it;
- association disposal must happen after best-effort report cleanup, not instead of cleanup.

## Evidence required for PASS

Physical success is not claimed by CI alone. Preserve the exact candidate SHA/artifact identity and collect:

1. ARSAS Diagnostic Export covering trusted-SCL association, RCB selection/activation, explicit startup GI, InformationReport reception and cleanup.
2. Matching Wireshark PCAP/PCAPNG for TCP port 102.
3. Screenshot or exported monitor evidence showing complete initial state and later event-driven updates.
4. Stop/Close evidence showing deterministic RCB release.

A PASS requires the software gates and the physical evidence to agree. If the wire capture contradicts UI/status text, the wire evidence is authoritative and the candidate remains blocked.
