# Control Panel live-model and interaction audit

## Reported field behavior

- Close is now fast and no longer flickers during Close command.
- Open can still show a short `Closed → Open → Closed` sequence immediately after an Open command.
- The Control model column remains `Auto-detect` until the user interacts with a row.
- Open/Close buttons are vertically clipped by the fixed compact DataGrid row height.
- Status-only rows still expose Technical details even though no command is available.
- Repeated clicks remain possible while a command is in progress.
- The command-panel helper sentence adds visual noise.
- Buttons need a tactile pressed-in response.

## Root causes

### Open feedback flicker

The previous source filter debounced only `CSWI*.Pos.stVal = Closed`. A relay can exhibit the same command-object echo in the opposite direction: publish the requested Open, briefly return the old Closed state, then publish the settled Open state. The direct command-result value could also reach the command row before stable live evidence.

### ctlModel remains Auto-detect

Discovered command rows begin as `Auto-detect on command`. Model inspection was primarily tied to opening the command panel or requesting a value, so the live model was not guaranteed to be resolved before the user saw the table.

### Clipped and unsafe command controls

The command DataGrid inherited a 34 px row height although a row contains both command buttons and result text. Button enablement was bound only to the global arm switch and did not include the row's `ControlIsBusy` state.

## Implemented correction

- Debounce both Open and Closed transitions on `CSWI*.Pos.stVal` for 350 ms before they enter the Value Viewer batch.
- Cancel a pending position sample when the opposite state arrives inside the guard window.
- Require fresh stable live-position evidence for both Open and Close command results.
- Preload unresolved `ctlModel` values for selected control objects immediately after their IED session is connected.
- Display compact Control model labels: `DO`, `SBO`, `Status only`, `Reading…`, or `Not available`; keep the full model as tooltip.
- Increase command-row height and give the Control column flexible width so Open/Close are not clipped.
- Replace Status-only Technical details with plain `Not available` and hide the secondary Details action.
- Bind all command actions to arm/test state, live model readiness, and `!ControlIsBusy`, disabling both Open and Close throughout an active command.
- Remove the helper sentence beginning `Current process value is shown...`.
- Add an application-wide pressed-in scale and vertical travel effect to every enabled button.

## Safety boundaries

- No automatic command retry.
- No repeated Select/SBOw or Operate.
- No change to ctlNum, origin, Test, Check, interlock, synchrocheck, or CommandTermination.
- XCBR/XSWI equipment-position updates remain immediate; only command-facing CSWI position transitions receive the short stability guard.
