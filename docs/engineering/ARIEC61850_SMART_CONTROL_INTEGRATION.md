# ARIEC61850 Smart Control integration audit

## Result

The uploaded engine now provides the control-service boundary that ArIED previously required. ArIED v1.6.4 integrates directly with `AR.Iec61850.Control` and removes the manual `Oper` / `SBOw` MMS-write path.

## Engine capabilities verified from source

- Validates a control Data Object root and rejects `ctlModel`, `ctlVal`, `Oper`, `SBO`, `SBOw`, and `Cancel` leaves.
- Reads live `ctlModel`.
- Retrieves exact live `Oper`, `SBOw`, and `Cancel` MMS specifications.
- Locates named `ctlVal` rather than guessing field position.
- Supports Direct Normal, SBO Normal, Direct Enhanced, and SBO Enhanced.
- Preserves one immutable `ctlVal`, origin, `ctlNum`, `T`, Test, and Check sequence.
- Supports typed SPC, DPC, INC/ISC, BSC, APC, and validated raw values.
- Decodes positive/negative CommandTermination, LastApplError, ControlError, and AddCause.
- Implements association-scoped control-object locking, selection timeout, Cancel, disposal cleanup, and association-loss outcomes.
- Blocks public generic MMS writes to `Oper`, `SBOw`, and `Cancel`.

## ArIED integration

ArIED now:

1. Opens the native control session for the selected Data Object.
2. Reads the native descriptor and current status.
3. Shows the detected DO/SBO sequence and exact `ctlVal` signature.
4. Converts user intent to a typed `Iec61850ControlValue`.
5. Calls `OperateAsync` with `AutoSelect=true`.
6. Waits for CommandTermination when required by enhanced security.
7. Correlates process feedback after successful service completion.
8. Displays ControlError, AddCause, LastApplError, `ctlNum`, sequence timestamp, and elapsed time.
9. Records the sequence result in Diagnostics.

## Safety boundary

`Send Command` is enabled only when the native descriptor reports `IsOperationallyReady=true` and the requested value can be represented safely by the live `ctlVal` type.

The following still require Windows/live validation before a production claim:

- Clean .NET 8 build and unit tests.
- All four control models against the simulator.
- DPC, SPC, INC/ISC, BSC, and APC live type variants.
- Positive and negative CommandTermination captures.
- Interlock, synchrocheck, test mode, selection timeout, Cancel, association loss, and competing-client cases.
- Multi-vendor relay evidence and process-feedback correlation.
