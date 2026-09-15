# Trusted SCL golden-wire physical trial

This document freezes the first field-test contract for the protocol-only ARSAS 1.6.36 trial lane.

## Immutable engine authority

- ARSAS branch: `trial/scl-golden-wire-v1636`
- ARIEC61850 engine: `e41def0a2676efb8a143905798155f6bccc6f047`
- Field-proven reporting/control baseline preserved by the engine lock: `11ab2304482600c19ba979f4fc9021ddb46b9af9`

## Gate 1 — read-only safe trial

Run the portable application with Wireshark capturing TCP port 102.

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

For Static DataSet report-only mode, ordered DataSet membership and RCB identity remain SCL-authoritative in memory. The trusted path does not perform a network DataSet-directory browse or create/delete a dynamic DataSet.

Primary activation expectation:

- BRCB: whole-RCB Read -> `RptEna=true` -> whole-RCB Read -> whole-RCB Read.
- URCB: whole-RCB Read -> `Resv=true` when exposed -> `RptEna=true` -> two whole-RCB readbacks.
- BRCB `ResvTms` is retry-only after a real direct-`RptEna` rejection.
- GI is not sent implicitly.

Physical success is not claimed by CI. JSON evidence plus Wireshark capture from the real IED are the acceptance evidence.
