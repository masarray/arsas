# P0 typed FC-root MMS evidence — 2026-10-18

Scope: read-only engineering value hydration. Sanitized protocol facts extracted
from the existing field evidence archive; no proprietary binaries or packet
payloads are committed. Not a field acceptance claim.

## Confirmed grouped MMS Read

A captured MMS Confirmed-Read request uses invoke ID 39 (0x27) and an
ordered list of **five** domain-specific named variables in a single request:

- `BCUGEF650 / SGgndPTOC1$CF0`
- `BCUGEF650 / SGgndPTOC1$DC0`
- `BCUGEF650 / SGgndPTOC1$EX0`
- `BCUGEF650 / SGgndPTOC1$SG0`
- `BCUGEF650 / SGgndPTOC1$ST`

The corresponding invoke-ID-39 Confirmed-Read response contains five
ordered, constructed MMS structure access results. The response uses
segmented ISO-on-TCP frames; proper COTP reassembly is required before
matching the invoke ID. This proves successful grouped FC-root acquisition
for that recorded transaction; it does not prove any particular scalar's
semantic field mapping, or that a different current association has identical
data.

## Failure in the consumer's previous read flow

The bounded Browser auto plan admitted only the first 32 leaves; the
FC-root flight passed those same 32 expected leaves to its type-verified
projector. A successful root containing additional GVA-verified children
was therefore **discarded outside the 32 queued targets**, even when those
children belong to the same operator-selected SGCB / LN. Increasing
the 32-target cap or adding retries does not fix this loss.

The bounded focused queue remains finite. The latest consumer change
passes exact selected-page live-model siblings for *already admitted FC
roots* through the existing typed schema/root decoder, and sends newly
proved children through the same read-result inbox and association-fenced
LiveValues ledger. The source model is the currently verified online model,
never SCL-only defaults. The path does not read unselected LNs, add SE/CO,
enable RCBs, issue an Operate, fabricate absent q/t, or start cyclic polling.
A root with more than 512 siblings is not silently truncated into a false
partial success.

## Remaining interoperability gap

The capture batches several FC-root variables in one Confirmed-Read.
The consumer's current root reader still issues typed GVA and root Read
individually. The engine already implements bounded batch Read codec;
a dedicated, physically validated grouped-root execution API is the next
engine slice. The current consumer fanout fix does not claim transport
batching parity.

Field gate: compare the exact FC, GVA tree order, MMS result type, and
source/association for `PIOCEna`, `InMagTyp`, `StrVal`, `RsMod`,
`StrMult`, `ExNumTr`, XCBR and RCBs; re-check old-successful controls.
