# Validation Checklist

## IED identity

- [ ] The card name changes from the temporary endpoint label to the real IEDName after discovery.
- [ ] Logical Device instances are shown separately from IEDName.
- [ ] Example domains such as `OCR7SR12CTRL` resolve to the expected IEDName/LD split.
- [ ] Multiple MMS domains from one IED produce one stable IEDName.
- [ ] Diagnostics records whether identity came from explicit model metadata, LDInst removal, common prefix, or heuristic fallback.

## Per-IED workflow

- [ ] Every card has independent connect/disconnect, start/stop, configure, and remove actions.
- [ ] Connecting or discovering IED B does not block monitoring of IED A.
- [ ] Stopping IED A does not change IED B.
- [ ] Signal Scanner is absent from the permanent main tabs.
- [ ] The selection wizard opens only for its owning IED.
- [ ] No signals are automatically selected on first discovery.
- [ ] Cancel restores the previous selection.
- [ ] Saved selection returns for the same IEDName.
- [ ] Saved selection returns by IP:port when IEDName matching is unavailable.
- [ ] Existing ArServer selections are imported when present.

## Reporting

- [ ] Existing static DataSet/RCB is attempted first.
- [ ] Actual DataSet members map to the correct selected object references.
- [ ] Partially uncovered static groups receive a dynamic recovery attempt.
- [ ] Points without static coverage receive a dynamic DataSet/URCB attempt before polling.
- [ ] Dynamic-monitor cleanup removes the temporary DataSet when supported.
- [ ] Only points still not covered by reporting enter cyclic MMS polling.
- [ ] Report-covered points use report-primary acquisition with only low-rate MMS verification/fallback when configured.
- [ ] GI provides an initial image when supported.
- [ ] BRCB/URCB reservation or write rejection produces a clear diagnostic.
- [ ] Acquisition identifies the actual RCB name, for example `Dynamic: URCBA01` or `Static: BRCBA02`.

## Process data

- [ ] Discovery animation overlays only the busy IED card and does not block other IEDs.
- [ ] Card indicator is red disconnected, green connected, and light-green TX pulses on report traffic.
- [ ] Live Values contains IED timestamp but no Local Time and no Age.
- [ ] Event Log contains only semantic value edges; quality-only changes never create rows.
- [ ] Event Log and CSV contain IED timestamp but no local receive timestamp.
- [ ] IEC Telegram removes the resolved IEDName prefix while preserving LD/LN/DO/DA.
- [ ] Diagnostics grid has no Local Time column.
- [ ] Breaker/switch double-point values decode correctly.
- [ ] Boolean protection start/operate/trip edges are correct.
- [ ] Analog magnitude and unit are correct.
- [ ] Missing quality is shown as unavailable, never promoted to Good.

## Large-signal behavior

- [ ] 10,000 points do not create 10,000 tasks or timers.
- [ ] Scanner rows are not retained as a permanent main-workspace grid.
- [ ] Poll scheduler CPU remains stable when most points are report-covered.
- [ ] Repeated reports use indexed reference lookup.
- [ ] Event bursts do not create one Dispatcher operation per update.
- [ ] WPF virtualization remains enabled.

## Recovery

- [ ] Cable removal affects only the owning IED session.
- [ ] Reconnect creates a fresh native client and rebuilds report plans.
- [ ] Saved signal selection remains available after disconnect/reconnect.
- [ ] No duplicate live points or poll entries appear after restart.
