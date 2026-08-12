# Relay time-synchronization and disturbance-recording procedure

This supplementary procedure is for protection relays in the approved FAT scope. It is not a blanket test for every IEC 61850 IED: BCU, bay controller, gateway, and other devices without a fault-record service are marked **N/A - capability not present**, not failed.

## 1. Scope and terminology

IEC 61850 carries device timestamps and `TimeQuality` information with values and reports. The clock-distribution mechanism remains an IED/project capability and may be SNTP/NTP, PTP/IEEE 1588 (including IEC/IEEE 61850-9-3), IRIG-B, or another approved source. ARSAS is the evidence client: it reads the relay timestamp/quality and records its own UTC capture time. ARSAS does not set the relay clock and does not act as an SNTP server.

The customer procedure must record the actual source configured in the relay, the required accuracy/tolerance, and the approved test reference. Do not infer SNTP merely because the device communicates over IEC 61850/MMS.

## 2. Time-synchronization test

### Preconditions

1. Confirm the relay role is protection/relay and identify the approved time source: SNTP/NTP, PTP, IRIG-B, or other project source.
2. Record the time-server or grandmaster identity, UTC/local-time setting, daylight-saving setting, and customer acceptance tolerance.
3. Synchronize the ARSAS laptop to the same approved reference. Do not manually change the laptop clock during the test.
4. Connect ARSAS to the relay and complete model discovery. Select a timestamped status/event point exposed by the relay.

### Test steps

1. Record laptop UTC (`ARSAS capture time`) and the relay timestamp from a fresh observation/event.
2. Record the relay `TimeQuality` state when available, including clock-not-synchronized and clock-failure indications.
3. Repeat at least three observations separated by the project-defined interval.
4. Calculate the observed offset as `relay UTC - ARSAS UTC`, allowing for network/processing delay and documented timestamp resolution.
5. Compare the maximum absolute offset and time-quality state with the approved project tolerance.

### Acceptance

Pass only when the relay reports a synchronized/healthy clock, the observed offset is within the approved tolerance, and the source identity is recorded. If the relay does not expose a usable timestamp or quality, record **Review / evidence unavailable**; do not manufacture an offset from laptop receive time. A BCU without the required protection/time evidence is **N/A**, unless the project explicitly includes it in the time test.

### Required report evidence

- IED name, IP address, role, firmware/configuration reference;
- time source type and source identity;
- relay timestamp and `TimeQuality` for each sample;
- ARSAS laptop UTC capture timestamp for each sample;
- calculated offset, maximum absolute offset, tolerance, and verdict;
- deviations, witness, and date/time of test.

## 3. COMTRADE / disturbance-recording test

This test is only for a protection relay with a discovered and usable fault-record service. It is **N/A - capability not present** for BCU or an IED that exposes no fault-record directory/files.

### Preconditions

1. Confirm the relay protection role and approved disturbance trigger/injection method. ARSAS does not inject a fault or operate a trip circuit.
2. Confirm the expected record format and companion files (normally COMTRADE `.CFG` and `.DAT`, with `.INF` where provided), naming convention, and retention policy.
3. Connect ARSAS to the relay and use the dedicated fault-record association to discover available records.

### Test steps

1. Apply the approved secondary-injection or simulated disturbance and record the trigger time/reference.
2. Allow the relay to close and retain the disturbance record.
3. Refresh fault-record discovery and identify the new record by name and timestamp.
4. Download the complete record set through ARSAS, preserving original filenames and transfer diagnostics.
5. Validate that companion files agree on station/channel metadata, sample rate, nominal frequency, trigger time, and channel count. Open the record with the approved COMTRADE viewer and confirm expected traces and trigger marker.
6. Repeat the download or retention check if the project requires a second record or overwrite/retention test.

### Acceptance

Pass when the expected new record is discoverable, the complete approved companion set downloads without unexplained gaps, the files parse in the approved viewer, and trigger/time metadata correlate with the test event. A partial, corrupt, or mismatched set is **Failed** or **Review** under the approved deviation process. A BCU or other device without fault-record capability is **N/A**, not failed.

### Required report evidence

- relay identity, role, and capability decision;
- disturbance trigger reference and relay record timestamp;
- discovered record name/path and file inventory;
- file sizes and SHA-256 hashes after download;
- COMTRADE parser/viewer validation result;
- correlation to the test event, deviations, witness, and date/time.

## 4. ARSAS boundary

ARSAS can preserve the relay timestamp, `TimeQuality`, local capture time, fault-record catalog, transfer diagnostics, and downloaded evidence. Final acceptance still depends on the approved FAT procedure, time-source/server configuration, test tolerances, injection equipment, and customer witness.
