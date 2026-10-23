# ARSAS Clock Sync — SNTP commissioning service

ARSAS includes a small clean-room SNTPv4 commissioning service for station-bus work. The implementation is written specifically for ARSAS from the protocol behavior described by RFC 4330 and RFC 5905; it does not embed or copy a third-party NTP implementation.

## Minimal operator UI

The compact main header contains a **NTP Server toggle**, a **dropdown of active PC IPv4 addresses**, and an **SNTP communication LED**. Technical request/reply counters, selected NIC and UTC reference limitations are provided as tooltips instead of permanent GUI text. Blue means listening, green means a reply was sent during the current session, amber means a pending request or service problem, and grey means stopped. No LED state alone proves that a relay is synchronized.

The lifecycle is independent of all IED sessions and uses C#/.NET async UDP, with the existing optional Npcap RAW fallback retained. No new native code, Windows service, or Windows Time configuration change is introduced. UI dispatcher notifications are coalesced, and per-client log notifications deduplicated before dispatch.

## Current behavior

- Starts only when the operator turns **NTP Server ON** in the main header, independently of IED/MMS/Discovery/FAT; it defaults OFF.
- Lists all active non-loopback PC IPv4 addresses, including multiple aliases on an adapter, for explicit server IP selection.
- Revalidates IP and network-adapter identity before binding; serialized rebind on IP change and fail-closed on ambiguous or missing addresses.
- Prefers a normal UDP/123 socket bound only to that local interface.
- If Windows Time or another process already owns UDP/123, automatically falls back to raw Ethernet capture/injection through Npcap on the same station-bus adapter.
- Never stops, restarts, or reconfigures Windows Time.
- Replies to SNTPv3/v4 client Mode 3 requests with server Mode 4.
- Copies the client Version, Poll, and Transmit timestamp into the reply fields required by SNTP server semantics.
- Sends an immediate SNTPv4 Mode 5 directed broadcast, then repeats every 64 seconds by default when a usable directed-broadcast address exists.
- No IED connection is needed for broadcast or unicast service; its startup broadcast occurs only after explicit operator enablement.
- Separately records `broadcast sent`, `client request seen`, and `Mode 4 reply sent` evidence.
- Advertises synchronized commissioning packets with commissioning compatibility `stratum 2` and reference ID `LOCL`.
- Performs a wall-clock sanity/step check. A large time step suppresses broadcast and makes that instant's unicast reply RFC-style unsynchronized (`LI=3`, `stratum=0`, `INIT`, server timestamps zero).
- Never fails an IEC 61850 association when SNTP cannot start.

## Evidence semantics

FAT Clock Sync telemetry intentionally distinguishes packet activity from actual relay synchronization:

- `B` / Broadcast: ARSAS transmitted a Mode 5 broadcast.
- `Req`: ARSAS observed a client Mode 3 request from an IED.
- `Reply`: ARSAS successfully transmitted a Mode 4 response.
- `sync not proven`: none of the three counters alone proves that the relay accepted the source or adjusted its internal clock.

A broadcast without a client request may still be valid when the relay is explicitly configured for broadcast NTP, but ARSAS does not treat it as an acknowledgement. For unicast SNTP, the strongest wire-level evidence is a Mode 3 request followed by a Mode 4 reply. Device-side time-quality or clock evidence is still required before declaring the relay synchronized.

## Commissioning compatibility stratum

Authorized field observations on the historical reference configuration showed that a conservative high-stratum local source could be rejected or remain marked unsynchronized. ARSAS therefore uses the same `stratum 2` advertisement for Mode 4 replies and Mode 5 broadcasts. This is a bounded commissioning default, not a claim about all IEDs. The original device-specific naming and observation context remain traceable in the [pre-migration source revision](https://github.com/masarray/arsas/commit/e03ff1caa7d83902ef106f0c4aafdc3fb24143e5).

The value is named in code as `SntpServerProfile.CommissioningCompatibilityStratum` and is protected by regression tests. It does not claim that the Windows laptop is physically traceable to a stratum-1 GNSS/PTP/atomic source. `LOCL` remains the reference ID and ARSAS diagnostics describe the laptop as a local commissioning source.

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

ARSAS binds to the **local PC IPv4 address selected in the toolbar**, not the route to a connected IED. A relay may request unicast time by using that address, regardless of whether it has an MMS association with ARSAS. An optional Mode 5 directed broadcast is emitted only after the operator switches the service ON.

Changing the selected local IP while enabled serializes Stop → fresh interface validation → Start. An IP removed from the computer cannot silently migrate the service to another station LAN. One ARSAS SNTP transport is active at a time; switching OFF stops it, and ARSAS closing disposes the service.

## Validation

`SntpPacketTests` covers:

- SNTP client request recognition;
- version/poll field copy behavior;
- Mode 4 reply semantics;
- originate timestamp echo;
- commissioning compatibility stratum 2 on unicast and broadcast packets;
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

## Standalone lifecycle regression

`GlobalSntpLifecycleRegressionTests` verifies the compact PC IP picker and independent lifecycle; `SntpLocalBindingTests` verifies alias selection on one NIC, rejection of stale/duplicate adapter addresses, loopback and unspecified IP guards. Windows CI also checks portable publish and smoke tests. Physical acceptance remains necessary: with zero connected IEDs, toggle ON, receive an SNTP Mode 3 client request, send a Mode 4 reply, toggle OFF, and verify UDP/123 is no longer held by ARSAS.\n