# ARSAS Clock Sync — SNTP commissioning service

ARSAS includes a small clean-room SNTPv4 commissioning service for station-bus work. The implementation is written specifically for ARSAS from the protocol behavior described by RFC 4330 and RFC 5905; it does not embed or copy a third-party NTP implementation.

## Current behavior

- Starts automatically after the first IPv4 IED reaches `IsConnected = true` while the FAT `Clock Sync` checkbox is enabled.
- Uses the Windows route to that IED to select the station-bus IPv4 interface.
- Prefers a normal UDP/123 socket bound only to that local interface.
- If Windows Time or another process already owns UDP/123, automatically falls back to raw Ethernet capture/injection through Npcap on the same station-bus adapter.
- Never stops, restarts, or reconfigures Windows Time.
- Replies to SNTPv3/v4 client Mode 3 requests with server Mode 4.
- Copies the client Version, Poll, and Transmit timestamp into the reply fields required by SNTP server semantics.
- Sends an immediate SNTPv4 Mode 5 directed broadcast, then repeats every 64 seconds by default when a usable directed-broadcast address exists.
- Sends another immediate broadcast when a newly connected IED is observed.
- Separately records `broadcast sent`, `client request seen`, and `Mode 4 reply sent` evidence.
- Advertises synchronized commissioning packets with SIPROTEC compatibility `stratum 2` and reference ID `LOCL`.
- Performs a wall-clock sanity/step check. A large time step suppresses broadcast and makes that instant's unicast reply RFC-style unsynchronized (`LI=3`, `stratum=0`, `INIT`, server timestamps zero).
- Never fails an IEC 61850 association when SNTP cannot start.

## Evidence semantics

FAT Clock Sync telemetry intentionally distinguishes packet activity from actual relay synchronization:

- `B` / Broadcast: ARSAS transmitted a Mode 5 broadcast.
- `Req`: ARSAS observed a client Mode 3 request from an IED.
- `Reply`: ARSAS successfully transmitted a Mode 4 response.
- `sync not proven`: none of the three counters alone proves that the relay accepted the source or adjusted its internal clock.

A broadcast without a client request may still be valid when the relay is explicitly configured for broadcast NTP, but ARSAS does not treat it as an acknowledgement. For unicast SNTP, the strongest wire-level evidence is a Mode 3 request followed by a Mode 4 reply. Device-side time-quality or clock evidence is still required before declaring the relay synchronized.

## SIPROTEC compatibility stratum

Field commissioning has shown that a conservative high-stratum local source can be rejected or remain marked unsynchronized on some SIPROTEC installations. ARSAS therefore uses `stratum 2` for both Mode 4 replies and Mode 5 broadcasts.

The value is named in code as `SntpServerProfile.SiprotecCompatibilityStratum` and is protected by regression tests. It does not claim that the Windows laptop is physically traceable to a stratum-1 GNSS/PTP/atomic source. `LOCL` remains the reference ID and ARSAS diagnostics describe the laptop as a local commissioning source.

If the Windows clock fails the ARSAS clock-health guard, synchronized stratum is not advertised: the affected unicast response becomes unsynchronized (`LI=3`, `stratum=0`, `INIT`) and broadcast is suppressed.

## Accuracy and trust boundary

ARSAS intentionally does not claim UTC traceability. The Windows system clock is treated as a temporary commissioning reference. ARSAS checks for gross time sanity and sudden wall-clock steps, but it does not claim the laptop is equivalent to GPS, IRIG-B, PTP, or an IEC/IEEE 61850-9-3 grandmaster.

A later phase can add explicit Windows upstream-source verification and/or device-side time-quality correlation without changing the SNTP packet engine.

## UDP/123 ownership and Npcap RAW fallback

Windows Time and other NTP software may already own UDP/123. ARSAS first attempts exclusive ownership of UDP/123 on the selected station-bus address. If that succeeds, normal Windows UDP sockets are used.

If the bind fails, ARSAS leaves the existing Windows service untouched and attempts an Npcap RAW fallback on the same adapter. The fallback:

- captures Ethernet frames matching `udp dst port 123`;
- accepts only supported IPv4 SNTP Mode 3 client requests addressed to the station-bus laptop;
- preserves a single incoming 802.1Q/802.1ad VLAN tag on the Mode 4 reply;
- builds Ethernet, IPv4 and UDP headers directly;
- calculates IPv4 and UDP checksums;
- replies directly to the request source MAC/IP/UDP port;
- injects Mode 5 broadcasts with Ethernet broadcast MAC and the route-derived directed-broadcast IPv4 address.

If both the normal socket path and Npcap fallback are unavailable, Clock Sync reports `PortUnavailable`; MMS/GOOSE/SV/FAT IEC 61850 behavior remains fail-open.

Npcap is therefore optional for the normal UDP path but required for the RAW fallback. The Windows installer warns when Npcap is not detected; it does not silently install drivers or modify Windows Time/firewall policy.

## Network scope

ARSAS serves the first station-bus interface selected by Windows routing. IEDs on that subnet can use unicast SNTP by configuring the ARSAS laptop station-bus IP as the server, or Mode 5 broadcast when the relay configuration supports broadcast-client operation.

If another connected IED routes through a different local IPv4 interface, ARSAS reports that the existing clock service remains on the original station-bus binding rather than silently moving the clock source.

## Validation

`SntpPacketTests` covers:

- SNTP client request recognition;
- version/poll field copy behavior;
- Mode 4 reply semantics;
- originate timestamp echo;
- SIPROTEC compatibility stratum 2 on unicast and broadcast packets;
- Mode 5 broadcast semantics;
- RFC-style unsynchronized response fields;
- directed-broadcast calculation;
- NTP timestamp round-trip accuracy.

`SntpEthernetFrameCodecTests` additionally covers:

- raw Ethernet Mode 3 recognition;
- MAC/IP/UDP endpoint swapping for Mode 4 replies;
- valid IPv4 and UDP checksums;
- preservation of a single VLAN tag;
- raw Mode 5 Ethernet/directed-broadcast construction;
- rejection of non-Mode-3 or wrong-destination-port traffic.
