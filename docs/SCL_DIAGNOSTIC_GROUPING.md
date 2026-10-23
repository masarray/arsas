# Grouped SCL diagnostics

ArIED groups repeated SCL findings before writing them to the live diagnostic log.

The grouping key is:

- finding code;
- IED or logical-device scope derived from the object reference.

For example, indexed report instances such as `A_BRCB`, `A_BRCB_1`, and `A_BRCB_2` under the same MMS domain are shown as one diagnostic group with:

- the total occurrence count;
- the representative engine message;
- up to three example object references.

A typical summary is:

```text
SCL_REPORT_DATASET_UNRESOLVED [E01BCU1ADD1]: 70 occurrences under E01BCU1ADD1. Examples: ...
```

The 40-row live-log limit now applies to diagnostic groups rather than raw findings. If more than 40 groups exist, the omitted message reports both the number of omitted groups and the raw findings represented by them.

The original typed findings remain attached to the engine workspace. Grouping changes presentation only; it does not change severity, suppress blocking findings, or reinterpret IEC 61850 semantics.

High and Error groups still raise the Diagnostics alert. Warning groups remain warnings and no longer appear as application errors.
