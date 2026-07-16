# GOOSE Subscriber

ArIED 61850 includes a read-only IEC 61850-8-1 GOOSE subscriber workspace. It captures raw Ethernet frames through the ARIEC61850 Npcap transport, decodes the GOOSE PDU, supervises stream sequence behavior, and maps each `allData` item to the corresponding ordered DataSet definition.

## Scope

The subscriber displays:

- APPID, source and destination MAC, VLAN ID/priority;
- `goCBRef`, `goID`, DataSet reference, `confRev`;
- `stNum`, `sqNum`, TimeAllowedToLive, test and `ndsCom` state;
- retransmission, state-change, duplicate, sequence-gap/regression, and TAL diagnostics produced by `ProcessBusStreamMonitor`;
- every decoded `allData` value in its exact frame order;
- signal name, object reference, FC, CDC/bType, previous value, and binding source.

The subscriber does not publish GOOSE, write MMS objects, enable GSEControl blocks, or change IED state.

## Model binding priority

ArIED builds one binding catalog whenever the GOOSE tab is opened, the model is refreshed, or capture starts.

1. **Loaded SCL workspace**
   - `SclGooseStream` identifies APPID, destination MAC/VLAN, `goCBRef`, `goID`, DataSet, and `confRev`.
   - `SclDataSetEntry.Index` defines the authoritative `allData` order.
   - ARIEC61850 performs native SCL-aware decoding when the SCL stream has a complete APPID, destination address, and resolved DataSet.
2. **Live MMS discovery**
   - `LiveIedControlBlockModel` identifies the discovered GOOSE control block and DataSet reference.
   - `LiveIedDataSetModel.Members`, ordered by `Index`, supplies signal references and FC information.
   - Discovered Data Attributes and ArIED signal metadata add CDC, bType, and operator-facing signal names where available.
3. **Unbound frame fallback**
   - The frame remains visible even when no SCL or discovery match exists.
   - Rows are named `Leaf 1`, `Leaf 2`, and so on, preserving wire order without inventing engineering semantics.

SCL is preferred when SCL and live discovery both match the same frame. Live discovery remains valuable when no usable SCL file is loaded or the station file does not contain the publishing stream.

## Ordered leaf semantics

The grid's `#` column is one-based for operators. Internally, it maps directly to zero-based GOOSE `allData` position and DataSet member index.

The displayed row count is the greater of:

- the number of values received in the frame; and
- the number of members expected by the matched model.

This makes count mismatches explicit. Missing values are shown as `<missing in frame>` rather than silently dropping model members. Extra frame values remain visible as unbound leaf positions.

## Runtime architecture

```text
Npcap adapter (promiscuous, read-only)
        │ BPF: EtherType 0x88B8, tagged or untagged
        ▼
NpcapProcessBusFrameSource
        ▼
ProcessBusStreamMonitor + GooseFrameParser
        │ sequence/TAL diagnostics + decoded values
        ▼
coalesced per-stream UI snapshot queue
        ▼
GOOSE stream grid → ordered DataSet leaf grid
```

Only the newest pending frame per stream is promoted to the WPF UI during a flush cycle. Packet counters and ARIEC61850 sequence supervision still observe every captured frame. This bounds UI work during retransmission bursts without changing protocol analysis.

## Requirements

- Windows 10 or Windows 11;
- Npcap installed and visible to the current user;
- ARIEC61850 `AR.Iec61850.Transports.Npcap` project available beside the ArIED repository;
- an approved laboratory, FAT/SAT, or commissioning network interface;
- administrator rights when required by the local Npcap installation policy.

The default BPF filter is:

```text
ether proto 0x88b8 or (vlan and ether proto 0x88b8)
```

## Field validation

Validate the feature with a known publisher and SCL before relying on names in project evidence:

1. Load the station SCD/CID or connect and complete live discovery.
2. Open **GOOSE Subscriber** and refresh the model summary.
3. Select the approved process-bus/station-LAN adapter.
4. Start capture and verify APPID, destination MAC, VLAN, `goCBRef`, DataSet, and `confRev` against the design.
5. Confirm leaf count and order against the DataSet FCDA/member list.
6. Trigger one controlled state change and verify `stNum` increments, `sqNum` restarts, changed values are highlighted, and retransmissions keep stable values.
7. Check Diagnostics for count mismatch, sequence regression, unexpected test/`ndsCom`, destination mismatch, or TAL expiry.

## Known boundaries

- Live MMS discovery may identify GOOSE control blocks and DataSet members without exposing the destination multicast MAC or VLAN; frame matching then relies on APPID and control/DataSet references.
- Vendor-specific structured DataSet members are rendered compactly as one `allData` item. The grid does not invent nested field names beyond metadata provided by ARIEC61850.
- Npcap capture visibility depends on Windows driver installation, adapter binding, VLAN offload behavior, and switch port mirroring/network topology.
- This workspace provides engineering evidence, not an IEC 61850 conformance certificate or cybersecurity assurance.
