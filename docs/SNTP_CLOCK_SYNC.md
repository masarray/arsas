# Standalone ARSAS SNTP Server

ARSAS serves **a selected local PC IPv4 address** on UDP/123 independently from IEC 61850 association, IED Discovery, Open SCL, GOOSE or FAT. This is a managed .NET lifecycle around the existing tested SNTP protocol and transport implementations.

## Operator workflow

1. In the main toolbar select a local computer IPv4 address in the dropdown. All active non-loopback unicast IPv4 addresses, including aliases on one NIC, are enumerated when the dropdown opens.
2. Switch **NTP Server** ON. ARSAS validates the selected address and adapter and starts its SNTP transport without opening any IED connection.
3. Switch OFF to stop all transport workers and release UDP/123. Changing the PC IP while enabled serializes Stop, revalidation and Start.
4. Communication dot: grey=off; blue=serving/waiting; amber=requests unanswered or service unavailable; green=at least one Mode-4 reply sent this run; red=fault. This reflects SNTP packet exchange, **not proof of relay clock synchronization**. Hover for details.

The service defaults **OFF** until the operator enables it. It stops when ARSAS closes. If an active PC IP disappears, refresh the dropdown to stop serving the stale interface. A disconnected or ambiguous binding cannot be silently moved to an unrelated NIC.

## Transport and time trust

The existing SNTPv3/v4 Mode-4 unicast and Mode-5 directed-broadcast implementations remain intact. On an explicit ON command, ARSAS tries to bind UDP/123 exclusively to the selected PC IP. If another service owns that socket, the existing Npcap RAW path may be used; Windows Time is not disabled or reconfigured. No new unmanaged implementation is introduced.

The service is a **local Windows-clock commissioning reference**, not a GPS receiver, UTC-traceable grandmaster or guaranteed time source for protection tests. Existing clock-health guards, compatibility stratum and device-side evidence requirements remain unchanged.

## Acceptance

- Start from zero IEDs, choose PC IP, switch ON: serving UDP/123 without waiting for MMS.
- Valid SNTP request to that PC IP gets the expected Mode-4 response; indicator responds to observed requests/replies.
- Switch OFF: service releases transport. Toggle ON/OFF repeatedly without duplicate workers or listeners.
- Change selected PC IP when serving: stop and start once with updated adapter; reject missing/ambiguous addresses.
- Leave IED connections, reports, control functions, GOOSE monitoring and FAT operationally unaffected.
