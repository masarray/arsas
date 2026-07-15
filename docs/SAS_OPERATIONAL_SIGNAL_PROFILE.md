# SAS operational signal profile

ArIED intentionally separates the complete IEC 61850 engineering model from the concise point list used by a substation automation system operator.

## Default visible points

The signal-selection window exposes only exact, operational value leaves that match common SAS use:

- switchgear positions: `CSWI/XCBR/XSWI.Pos.stVal`;
- interlocking permissions: `CILO.EnaOpn/EnaCls.stVal`;
- protection indications: standard `Str.general`, `Op.general`, `Tr.general`, and breaker-failure `OpEx.general` where the exact Data Attribute exists;
- fundamental metering: phase current, phase/phase-to-phase voltage, frequency, active/reactive/apparent power, and power factor;
- transformer/tap-controller operational status and tap position;
- project-specific GGIO indications, analogue inputs, and standard GGIO controllable outputs;
- validated operational control objects such as switchgear `Pos` and tap raise/lower commands.

## Hidden engineering detail

The default list does not expose quality/timestamp sidecars, nameplate/configuration attributes, `Mod/Beh/Health`, report-control attributes, control service leaves, min/max/mean/demand/harmonic detail, protection phase-detail attributes, or guessed child leaves created from a shallow Data Object.

The full typed live model remains attached to the IED session and is still available to SCL export and diagnostics. The filtering changes only the operator-facing signal inventory.

## Evidence rule

A proposed leaf from the smart read planner is accepted only when the live VAA/type tree contains the exact Data Attribute. Exact MMS directory leaves and exact SCL Data Attributes are also accepted. A fallback or inferred leaf is hidden unless a direct MMS proof-read has confirmed it.
