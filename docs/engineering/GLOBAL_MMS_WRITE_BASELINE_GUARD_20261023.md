# Global MMS Write / Browser Control — protected ARSAS baseline
Date: 2026-10-23 (Asia/Jakarta)

## Exact consumer starting point

- Integrated PR: https://github.com/masarray/arsas/pull/679
- Integration branch: `fix/fat-integrated-quality-attention-20261022`
- **Protected ancestry anchor:** `c3555b639de0bbed14fdd06b55683479cf25bd31`
- This feature branch was created from that **exact SHA**, not `main`, a cached ZIP snapshot or the older `10b3ebf` checkpoint.
- PR #679 was draft at the time of verification. Before merging or rebasing, re-read live PR HEAD, compare ancestry, review overlapping files and preserve newer unrelated changes. No force-push.

## Already integrated — protect from regressions

| Area | Proven ancestor commit | Current source/contract |
| --- | --- | --- |
| Compact SMV waveform / phasors / Measurements | `84cbc36089bf`, `b446e9abf92e` | `Views/SmvInlineViewer.xaml`, `Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs`, `WaveformPanelControl.cs` |
| Verified SCL-derived IP Discovery IEDName | `4474e490389b` | `Services/Iec61850MonitorRuntime.cs`; provisional if not SHA-reverified |
| Stable Open/Close width in Command Dock | `559307216fe7` | `MainWindow.xaml`: both position buttons `Width=72, MinWidth=72` |
| Command Dock internal scroll and viewport constraints | `fbe205b9161`, `10b3ebf73a9` | `MainWindow.WorkbenchP1PanelGeometry.cs`; `EquipmentCommandGrid` ScrollViewer Auto |
| Verified control list, equipment ahead of GGIO and no CILO Operate | `c0845d28867`, `96b3128bee7`, `1f82e286807` | `MainWindow.IecNaturalSort.cs`, `tests/ARSAS.Tests/MultiGridNaturalSortRegressionTests.cs` |
| Idempotent control list refresh / anti flicker | `19580fd708c`, `7b9f983491e` | Preserve identity/incremental update; never reset unchanged rows |
| Typed exact model Read including single-flight ST/SE | `4935a63d3963`, `98df880ca62d`, `f2df9814fc50` | `Services/IedBrowserTypedDoReadFlight.cs`, `Models/IedBrowserReadWireShapeEvidence.cs` |
| Command Dock scroll anchor recovery | `97bd92652ac0` | `MainWindow.CommandPanelUx.cs` |

GitHub commit comparisons against consumer anchor showed **behind=0** for all selected feature ancestors above. This proves ancestry and source presence, not independent physical UI acceptance.

## Engine qualification separation

- Production lock: `engines/ARIEC61850.lock.json` pins `9c7cb40364023a7b217e5ec31ab3105ba90142eb`.
- Vendor-neutral prefixed-LN trial: engine PR #189 SHA `24aae4698026f3a5cf7a35225c5d8000018af3e4`, **closed, not merged**; experimental only.
- Generic MMS Write response cardinality work: engine PR #195 (draft), **not** an approved production-engine pin update.
- Never silently replace trial features with the production engine, nor promote experimental engine into production lock. A controlled cross-repo Windows build needs explicit exact-SHA mapping.

## Mandatory guard before next implementation change

1. Fetch live PR #679 and feature branch HEAD and compare them; review overlaps if integration branch has advanced.
2. Keep new feature changes isolated to typed Global Write + Browser Control paths. Reuse current `NativeIec61850Client`, `Iec61850MonitorRuntime`, `ControlCommandWindow`, `ControlModelWriteWindow`, existing `MmsWrite`, and session/authority gates.
3. Run existing Command Dock WPF sorting/geometry, SV viewer, IEDName, model-read, R7 and Windows CI tests on **final new SHA**; no pass inherited from a different SHA.
4. Preserve StatusOnly and CO generic-Write denial; no automatic Select/Operate, silent retry/rollback, physical FAT claim, or production pin change.
5. Any new runtime UI control must keep Command Dock independent scrolling and fixed Open/Close button dimensions.
