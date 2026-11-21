# ARIEC61850 control-service overhaul required by ArIED 61850

## Decision

The current ARIEC61850 source exposes generic MMS discovery, read, and write primitives, but it does not yet expose a complete IEC 61850 client-side control-object service.

ArIED must not treat a hand-built MMS write to `Oper` or `SBOw` as a production-grade control implementation. Version 1.5.8 therefore keeps command-object discovery and inspection, but disables **Send Command** until the engine provides the native service described below.

## Why generic MMS write is insufficient

A compliant control sequence has to preserve the exact server-defined control-object type and semantics. Depending on `ctlModel`, the client must execute one of these sequences:

- Direct operate, normal security: `Oper`
- Select-before-operate, normal security: `SBO` read/select, then `Oper`
- Direct operate, enhanced security: `Oper`, then wait for command termination
- Select-before-operate, enhanced security: `SBOw`, then `Oper`, then wait for command termination

The client also has to manage:

- Exact `ctlVal` MMS type from the live variable specification
- Consistent origin, `ctlNum`, `T`, Test, and Check values across one control sequence
- Interlock-check and synchrocheck bits
- Time-activated operation when supported
- SBO timeout and cancellation
- Positive and negative command termination
- `LastApplError`, control error, and `AddCause`
- Enhanced-security completion, not only the immediate MMS write response
- Concurrency protection so only one sequence owns a control object at a time
- Safe cancellation and association-loss cleanup

## Required engine namespace and contract

Recommended namespace:

```csharp
namespace AR.Iec61850.Control;
```

Recommended public surface:

```csharp
public interface IIec61850ControlService
{
    Task<Iec61850ControlObjectSession> OpenAsync(
        MmsClientSession session,
        string objectReference,
        CancellationToken cancellationToken);
}

public sealed class Iec61850ControlObjectSession : IAsyncDisposable
{
    public Iec61850ControlObjectDescriptor Descriptor { get; }

    public Task<Iec61850ControlActionResult> SelectAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken);

    public Task<Iec61850ControlActionResult> SelectWithValueAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken);

    public Task<Iec61850ControlActionResult> OperateAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken);

    public Task<Iec61850ControlActionResult> CancelAsync(
        CancellationToken cancellationToken);
}
```

The descriptor must contain at least:

```csharp
public sealed class Iec61850ControlObjectDescriptor
{
    public string ObjectReference { get; init; }
    public string Cdc { get; init; }
    public Iec61850ControlModel ControlModel { get; init; }
    public MmsVariableSpecification CtlValSpecification { get; init; }
    public string StatusReference { get; init; }
    public TimeSpan? SboTimeout { get; init; }
    public TimeSpan? OperTimeout { get; init; }
    public bool SupportsTimeActivatedOperate { get; init; }
}
```

The final result must distinguish service acceptance from command completion:

```csharp
public sealed class Iec61850ControlActionResult
{
    public bool RequestAccepted { get; init; }
    public bool CommandTerminationReceived { get; init; }
    public bool PositiveTermination { get; init; }
    public string ClientError { get; init; }
    public string ControlError { get; init; }
    public string AddCause { get; init; }
    public string LastApplErrorText { get; init; }
    public string RequestHex { get; init; }
    public string ResponseHex { get; init; }
}
```

## Discovery repair required in the engine

The engine must return one descriptor per controllable **Data Object**, for example:

```text
OLSF501CB1/CSWI1.Pos
```

It must never publish these leaves as separate command objects:

```text
CSWI1.ctlModel
CSWI1.ctlVal
CSWI1.ctlNum
CSWI1.stSeld
CSWI1.Oper
CSWI1.SBO
CSWI1.SBOw
CSWI1.Cancel
```

The control root should be reconstructed from the live model hierarchy and variable-access specification, not guessed only from leaf names.

## Tests required before ArIED enables live command

1. DPC direct normal Open and Close.
2. DPC SBO normal Select → Operate.
3. DPC direct enhanced with CommandTermination+.
4. DPC enhanced rejection with CommandTermination- and decoded AddCause.
5. SBO enhanced SelectWithValue → Operate.
6. SPC On/Off.
7. INC/ISC Raise and Lower.
8. BSC target position using exact ValWithTrans structure.
9. APC with integer and floating AnalogueValue variants.
10. Test=true command that does not move the process.
11. Interlock and synchrocheck rejection.
12. Select timeout and Cancel.
13. Association loss during an active sequence.
14. Two concurrent clients competing for one SBO object.
15. Simulator evidence for request, response, command termination, and process feedback.

## ArIED integration rule

ArIED should enable **Send Command** only when the engine reports:

- a valid control-object root;
- a known `ctlModel`;
- an exact `ctlVal` type specification;
- a native sequence executor;
- command-termination support for enhanced security.

Until then, ArIED may display and inspect command objects, but it must remain read-only.
