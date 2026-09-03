# SCL signal selection modal design QA

## Visual sources

- User reference / previous implementation: attached visual `codex-clipboard-090813c8-4eb5-4d1a-a394-dd81fe44b6f0.png`
- Runtime capture of the new WPF modal: `output/scl-signal-selection-modern.png`
- Side-by-side comparison: `output/scl-signal-selection-comparison.png`

## Current scope

The native `MessageBox` used after SCL import was replaced by `SclSignalSelectionModeWindow`, a modal WPF card that uses the existing ARSAS application icon, typography, color tokens, button styles, Lucide resources, elevation, and rounded-card language.

## Comparison findings

- The previous paragraph-heavy system dialog now has a clear title, shared-workspace subtitle, IED count badge, two explicit selection cards, and a supporting note.
- `Use Static DataSet` is selected by default and visibly marked as recommended; `Choose Signals Manually` is an equally accessible radio-card choice.
- Generic Yes/No labels are replaced by Cancel and Continue, with default and cancel keyboard behavior retained.
- Spacing, 17–24 px corner radii, subdued borders, blue selection state, Aptos/Segoe typography, and application button styles match existing ARSAS pop windows such as Save SCL.
- The runtime screenshot shows no clipped copy, overlapping controls, broken padding, or truncated actions at the captured Windows scaling.
- Both cards are fully clickable, mouse/pressed/focus states are defined, Enter continues, Escape cancels, and closing the window leaves the imported Engineering model unchanged.

## Functional companion checks

- Engineering Open SCL now permits multiple file selection and adds every distinct file into the same Engineering workspace.
- A single signal-authority decision applies to all newly imported IED workspaces and is bridged into an already-loaded FAT workspace without a second prompt.
- Duplicate FAT sources are filtered by SHA-256 before append; existing connected or monitoring IEDs are retained.

## Current validation

- Release build: passed with 0 errors using the ARIEC61850 commit pinned by `engines/ARIEC61850.lock.json`.
- Test suite: 616 passed, 0 failed, 0 skipped.
- Runtime WPF capture and side-by-side visual inspection: passed.

Final result for the SCL signal selection modal: **passed**.

---

# Previous IED protection relay fascia design QA

## Scope

- Source artwork: `Assets/ied-protection-relay-fascia.png`
- Reusable WPF consumer: `IedRelayFrontPanelTemplate` in `App.xaml`
- Runtime surfaces: IED Explorer (`MainWindow.xaml`) and IO List FAT (`IoListTestingWindow.xaml`)
- Production icon size: 50 × 50 device-independent pixels

## Implementation checks

- The dedicated relay fascia PNG is packaged as a WPF resource and reused on both IED card surfaces.
- The former calculator-style inline path is absent from both views.
- LIVE/STOP state remains data-driven and is rendered as a compact label below the relay artwork.
- The state label is no longer layered over the fascia or LCD.
- The IED Explorer icon has no `DropShadowEffect`; status is conveyed by the restrained label and state rail only.
- IO List FAT keeps the final `✔ PASS` result badge in the text area while operational LIVE/STOP state stays below the icon.

## Validation

Verified on GitHub Actions using Windows Server 2025 and .NET SDK 8.0.423:

```powershell
dotnet build ArIED61850Tester.csproj -c Release
dotnet test tests/ARSAS.Tests/ARSAS.Tests.csproj -c Release --filter "FullyQualifiedName~IoTestingUiContractTests"
```

Results:

- Release build: passed with 0 errors. The 12 reported nullable-reference warnings pre-existed in unrelated connection-classifier and signal-viewer files.
- Focused UI contracts: 11 passed, 0 failed, 0 skipped.
- XAML parse checks: passed for `MainWindow.xaml` and `IoListTestingWindow.xaml`.

The focused UI contract suite includes structural checks that both card surfaces use the shared fascia, keep status labels outside the artwork, remove the icon-level shadow, and do not restore the old calculator path.

## Visual QA status

The source asset itself is repository-visible and auditable. A fresh full-window runtime screenshot remains recommended as P3 evidence for spacing at every Windows scaling factor; it is not represented by inaccessible machine-local paths in this document.

Final result: code structure, Release build, and 11/11 UI contracts passed; runtime screenshot evidence remains a follow-up polish item.
