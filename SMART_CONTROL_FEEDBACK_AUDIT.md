# Smart Control feedback and latency audit

## Reported behavior

- `CSWI1.Pos` changed in the IED, but ArIED reported feedback timeout.
- The UI displayed On/Off and an SPC badge for a breaker position object.
- Close took roughly four seconds to appear in `Pos.stVal`; Open appeared in under one second.
- The control-service result showed positive CommandTermination and approximately 429 ms elapsed.

## Root cause 1 — command CDC and status CDC were conflated

The current ARIEC61850 descriptor infers `Cdc` from the live `ctlVal` MMS type. On this relay, `CSWI1.Pos.Oper.ctlVal` is Boolean, so the engine reports SPC. The process status `CSWI1.Pos.stVal`, however, is a two-bit double-point status. The previous ArIED code compared the feedback as Boolean SPC and therefore could not match raw Dbpos text such as `bits(80, unused=6)`.

ArIED v1.6.4 resolves the semantic control family from the object identity. `CSWI`, `XCBR`, and `XSWI` `Pos` objects are treated as position/DPC controls for user interaction and status feedback, while the exact live Boolean `ctlVal` is still used on the wire.

## Root cause 2 — duplicated control-object discovery

The old popup inspected a control object, then command execution opened and discovered the same object again. That repeated ctlModel read, live Oper/SBOw type retrieval, timeout reads, and domain-variable discovery.

ArIED v1.6.4 caches the native control-object session per IED association. Repeated commands reuse the validated descriptor until disconnect/reconnect.

## Latency interpretation

Three times are now shown separately:

1. **Control service** — Select/SBOw, Operate, MMS acceptance, and CommandTermination as required.
2. **Process feedback** — time until the status object reaches the requested state.
3. **Total** — end-to-end time.

A positive CommandTermination at about 429 ms means the IEC control sequence itself completed quickly. A later `Pos.stVal` transition is normally downstream process logic or equipment feedback delay. ArIED does not hide that delay; it now reports it separately and waits up to 12 seconds for position feedback.

## Engine recommendation

No full ARIEC61850 rewrite is required. The engine API should eventually distinguish:

- command-wire `ctlVal` type/CDC; and
- status/feedback semantic CDC.

For example, a descriptor could expose `CommandCdc`, `StatusCdc`, and `SemanticKind=Position`. The app compatibility resolver remains necessary until that contract is available.
