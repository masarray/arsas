# ARSAS Clock Sync — SNTP P0

ARSAS includes a small clean-room SNTPv4 commissioning service for station-bus work. The implementation is written specifically for ARSAS from the protocol behavior described by RFC 4330 and RFC 5905; it does not embed or copy a third-party GPL NTP implementation.

## P0 behavior

- Starts automatically after the first IPv4 IED reaches `IsConnected = true`.
- Uses the Windows route to that IED to select the station-bus IPv4 interface.
- Binds only that local interface on UDP/123.
- Replies to SNTPv3/v4 client requests with server mode 4.
- Copies the client Version, Poll, and Transmit timestamp into the reply fields required by SNTP server semantics.
- Sends an immediate SNTPv4 mode-5 directed broadcast, then repeats every 64 seconds by default.
- Sends another immediate broadcast when a newly connected IED is observed.
- Records client request observations by source IP so ARSAS can distinguish `request observed` from merely `broadcast advertised`.
- Advertises synchronized commissioning packets with SIPROTEC compatibility `stratum 2` and reference ID `LOCL`. This is intentionally below the SIPROTEC questionable-stratum boundary used in Siemens time-quality handling, while avoiding a false `stratum 1` primary-reference claim.
- Performs a wall-clock sanity/step check. A large time step suppresses broadcast and makes that instant's unicast reply RFC-style unsynchronized (`LI=3`, `stratum=0`, `INIT`, server timestamps zero).
- Never fails an IEC 61850 association when SNTP cannot start.

## SIPROTEC compatibility stratum

SIPROTEC devices evaluate the quality of the SNTP server in addition to timestamp fields. Field commissioning has shown that a conservative high-stratum local source can be rejected or remain marked unsynchronized on SIPROTEC installations. ARSAS therefore forces its current commissioning profile to `stratum 2` for both mode-4 unicast replies and mode-5 broadcasts.

The value is named in code as `SntpServerProfile.SiprotecCompatibilityStratum` and is protected by regression tests. It is not used to claim that the Windows laptop is physically traceable to a stratum-1 GNSS/PTP/atomic source. `LOCL` remains the reference ID and ARSAS diagnostics describe the source as a local commissioning clock.

If the Windows clock fails the ARSAS clock-health guard, synchronized stratum is not advertised: the affected unicast response becomes unsynchronized (`LI=3`, `stratum=0`, `INIT`) and broadcast is suppressed.

## Accuracy and trust boundary

P0 intentionally does not claim UTC traceability. The Windows system clock is treated as a temporary commissioning reference. ARSAS checks for gross time sanity and sudden wall-clock steps, but it does not claim the laptop is equivalent to GPS, IRIG-B, PTP, or an IEC/IEEE 61850-9-3 grandmaster.

A later phase can add explicit Windows upstream-source verification and/or PTP monitoring without changing the SNTP packet engine.

## UDP/123 ownership

Windows Time and other NTP software may already own UDP/123. ARSAS does **not** stop or reconfigure those services automatically. ARSAS requests exclusive ownership of UDP/123 on the selected station-bus address; if bind fails, Clock Sync reports `PortUnavailable` and all MMS/GOOSE/SV behavior continues normally.

A raw/Npcap transport can be added later as a separate phase for advanced multi-NIC/coexistence scenarios. P0 intentionally keeps the time service isolated and auditable.

## Network scope

P0 serves the first station-bus interface selected by Windows routing. IEDs on that subnet can use either unicast SNTP (configure the ARSAS laptop IP as server) or mode-5 broadcast if their vendor configuration supports broadcast client mode.

If another connected IED routes through a different local IPv4 interface, ARSAS reports that the existing clock service remains on the original station-bus binding rather than silently moving the clock source.

## Validation

`SntpPacketTests` covers:

- SNTP client request recognition;
- version/poll field copy behavior;
- mode-4 reply semantics;
- originate timestamp echo;
- SIPROTEC compatibility stratum 2 on unicast and broadcast packets;
- mode-5 broadcast semantics;
- RFC-style unsynchronized response fields;
- directed broadcast calculation;
- NTP timestamp round-trip accuracy.
