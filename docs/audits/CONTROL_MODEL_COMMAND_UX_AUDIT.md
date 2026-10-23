# IEC 61850 ctlModel and command UX audit

## Reported symptoms

1. A Data Object with `ctlModel=StatusOnly` was still rendered with Open/Close buttons.
2. Inspecting that object produced a red `InvalidOperationException` even though `StatusOnly` is a valid IEC 61850 control-model value.
3. On an SBO object, the command value could briefly show **Closed** and then return to **Open** in the row, while a second attempt appeared to work.

## Root cause

### Status-only object

The ARIEC61850 Smart Control service reads the live `ctlModel` correctly. It intentionally refuses to create an executable control session for `StatusOnly` and reports `ctlModel=StatusOnly` in the exception.

ArIED previously treated that exception as a generic inspection failure. The command-row layout was selected only from the semantic CDC/object name, so a `CSWI/XCBR/XSWI.Pos` row still received Open/Close controls even after the IED had explicitly declared the object read-only.

### SBO value appears to revert

The command verifier and the live report/poll monitor are independent producers of `ControlCurrentValue`:

- the control sequence publishes its final feedback observation;
- the normal monitor continues to publish report or cyclic samples every UI batch.

A sample requested before process movement can arrive after a newer command sample and overwrite the badge. This is a presentation race; the audited SBO/SBOw engine still executes one immutable Select/Operate sequence and this patch does **not** add an automatic retry.

## Changes

- Parse all five live `ctlModel` states from the native model text or inspection evidence:
  - Status only
  - Direct Operate, normal security
  - Select Before Operate, normal security
  - Direct Operate, enhanced security
  - Select Before Operate, enhanced security
- Treat `StatusOnly` and unresolved `Unknown` as read-only states.
- Derive row action visibility from the resolved `ctlModel`, not only the CDC/object name.
- Replace the status-only inspection error with a normal informational diagnostic.
- Coalesce command-row feedback while `ControlIsBusy`; publish the latest observation when the operation completes so a stale report/poll sample cannot create a false Close→Open flicker.
- Show the active SBO sequence in the row result (`SBO Select → Operate` or `SBOw → Operate`).
- Preserve the safety model: no command retry, no bypass of Live control armed, and no change to the native IEC 61850 command sequence.

## Required live validation

- `ctlModel=0`: model column says **Status only** and no Open/Close/On/Off/Set command is shown.
- `ctlModel=1`: **Direct Operate (DO) • Normal security** and the semantic command buttons remain available.
- `ctlModel=2`: **Select Before Operate (SBO) • Normal security** and one click performs Select→Operate.
- `ctlModel=3`: **Direct Operate (DO) • Enhanced security** and the result reflects CommandTermination.
- `ctlModel=4`: **Select Before Operate (SBO) • Enhanced security** and one click performs SBOw→Operate→CommandTermination.
- During one SBO command, the value badge does not briefly publish an older report/poll sample.

If the physical IED really changes Closed→Open after this UI stabilization, capture the MMS Oper response, LastApplError/CommandTermination, status report, and event log. That would prove an IED/interlocking/process-state behavior rather than the presentation race fixed here.
