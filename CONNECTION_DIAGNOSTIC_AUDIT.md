# ArIED 61850 v1.6.4 — Connection and Route Audit

## What the supplied report proves

The application did not reach TPKT, COTP, ACSE, MMS Initiate, model discovery, or RCB handling. The failure is at TCP routing/listening level.

Target:

```text
192.168.1.5:102
```

Active test Ethernet addresses:

```text
KM_Test = 192.16.1.157/24, 192.16.1.158/24
```

Those addresses are in `192.16.1.0/24`, not `192.168.1.0/24`. The active Wi-Fi interface is `192.168.1.8/24`, so Windows will normally route `192.168.1.5` through Wi-Fi. If the relay is physically connected to `KM_Test`, correct the Ethernet address to an unused address in the relay subnet, for example `192.168.1.157/24`, and temporarily disable Wi-Fi or set an explicit route/interface metric.

## Refused versus timeout

The captured native attempt returned TCP `ConnectionRefused`, while the later diagnostic probe timed out. This means the endpoint/network path did not behave consistently between attempts. Common causes are a changing route, IED reboot/service restart, cable/VLAN changes, or a different host being reached through another interface.

## Safe next checks

1. Correct the test Ethernet subnet (`192.16` versus `192.168`).
2. Temporarily disable Wi-Fi so there is only one route to `192.168.1.0/24`.
3. Verify `ping 192.168.1.5` and `Test-NetConnection 192.168.1.5 -Port 102`.
4. Close IEDScout and other MMS clients if the relay has a small association limit.
5. Retry ArIED and copy the new diagnostic report.

## Shutdown defect

The previous `async void Window_Closing` cancelled the close, awaited runtime disposal, and called `Close()` from the same closing event lifecycle. WPF can still mark the Window as closing at that point, producing `VerifyNotClosing`.

v1.6.4 schedules cleanup after the first Closing event returns and then uses `Application.Shutdown()` with a re-entry guard.
