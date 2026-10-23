# Global Live Monitor, compact IED card, and diagnostic-noise audit

## Field evidence

The app was tested from main commit `670778e6b881d9b6238fc0ba169ad4c57422a15c` (PR #6). The screenshots and support diagnostic show three independent issues:

1. Global Live Monitor filter values are accepted and filtering runs, but typed text is not visibly rendered.
2. The global grid uses fixed pixel widths, leaving unused space at the right edge instead of stretching across the panel.
3. The runtime compact-card transformation does not reliably locate the realized DataTemplate elements, so the original verbose IED card remains visible.

The diagnostic journal also marks several expected operations as WARN:

- every normal `Control requested` audit entry;
- a successful smart RCB fallback;
- a control-adjacent InformationReport whose RptID/DataSet identity is ambiguous and is intentionally rejected by the engine.

A separate `MMS Confirmed-Write returned 1 failure(s)` entry is a genuine command rejection and must remain ERROR.

## Global Live Monitor correction

- Replace the external watermark overlay with the same TextBox template behavior used by the Selection Wizard:
  - 36 px height;
  - 12.5 px text;
  - `#F8FAFC` idle background;
  - gray top divider;
  - white background and 2 px blue top line on focus;
  - watermark visible only while empty and unfocused.
- Bind the foreground explicitly into `PART_ContentHost` so typed characters remain visible.
- Replace all fixed pixel column widths with weighted star widths and practical minimum widths. The complete filter/header/grid now stretches to the full panel while retaining horizontal scrolling on narrow windows.

## Compact IED card correction

The card transformer now locates named DataTemplate elements (`CardContent`, `ConnectionDot`, `UnreadEventBadge`) rather than depending on one specific parent chain. It runs for every realized container through `LayoutUpdated` and the item generator.

Normal card content is reduced to:

- technical IED icon with connection/report dots;
- IED name;
- endpoint `IP:port`;
- Play, Stop, Edit, and Remove icon buttons.

The logical-device list, scanned/selected/live counters, acquisition sentence, unread badge, and rounded action tray are hidden. The existing busy/discovery overlay remains unchanged.

## Diagnostic interpretation and correction

### Normal audit entries

`Control requested` is evidence that a user initiated a command. It is not a failure and is now INFO.

### Successful smart RCB fallback

The preferred MMXU RCB was not selected because the runtime found a safer free RCB under LLN0. All eight selected points were subsequently report-covered and MMS fallback was zero. This is a successful routing decision and is now INFO.

### Ambiguous control-adjacent InformationReport

The relay emits one InformationReport around several control operations without a unique RptID/DataSet identity. The engine correctly refuses to project it onto an arbitrary DataSet. When this occurs within three seconds of a known control request/completion, ArIED now emits at most one compact INFO notice per 30 seconds and suppresses repeats. The same condition outside a control window remains WARN because it may represent an unrelated process-report routing problem.

### Genuine command rejection

The single Close attempt rejected with `MMS Confirmed-Write returned 1 failure(s)` remains ERROR. It was followed by successful Open/Close operations, so the session and SBOw control model remained healthy; the individual write was rejected by the IED and must not be hidden.

## Safety boundaries

- No change to IEC 61850 control execution, SBOw sequence, ctlNum, origin, Test, Check, CommandTermination, RCB setup, or MMS polling.
- No unsafe report projection is introduced.
- Unrelated ambiguous reports remain warnings.
- Real command rejections remain errors.
