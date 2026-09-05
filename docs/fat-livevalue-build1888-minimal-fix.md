# FAT live-value minimal fix — Build #1888 baseline

This recovery is intentionally based on the exact physically bench-tested ARSAS revision:

- ARSAS commit: `712e2c557d5da83d2f02b81f3a58905e985d1e50`
- Build ARSAS: `#1888`
- Original portable EXE SHA-256: `063751919b51ef1bf13f666441ec2fff7e1f5a54a5b647295c158abbd732a0d6`

Only two observed defects are in scope:

1. FAT LIVE VALUE / QUALITY can remain stale after the Engineering live point changes.
2. WPF row recycling can briefly show a different row's value while scrolling.

The fix therefore only:

- mirrors presentation fields from the already-filtered Engineering live image on the existing WPF UI-flush clock; and
- keeps row virtualization enabled while changing the realized-row mode to non-recycling `Standard`.

Explicitly out of scope and unchanged from Build #1888:

- FAT Value 1 / Value 2 evidence and auto-capture;
- command panel, Interlock / Sync defaults, confirmation and failure overlay;
- toolbar / Stop layout and Engineering return navigation;
- FAT session lifecycle and Start / Pause / Resume / Stop;
- Static DataSet / RCB / GI / InformationReport acquisition;
- MMS polling policy;
- SCL selection and shared Engineering/FAT workspace authority.

Do not merge until a physical bench retest proves that Build #1888 behavior is preserved and LIVE VALUE remains stable during fast scrolling.
