# IED protection relay fascia design QA

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

The patch branch is committed only after these Windows checks pass:

```powershell
dotnet build ArIED61850Tester.csproj -c Release
dotnet test tests/ARSAS.Tests/ARSAS.Tests.csproj -c Release --filter "FullyQualifiedName~IoTestingUiContractTests"
```

The focused UI contract suite contains 11 tests, including structural checks that both card surfaces use the shared fascia, keep status labels outside the artwork, and do not restore the old calculator path.

## Visual QA status

The source asset itself is repository-visible and auditable. A fresh full-window runtime screenshot remains recommended as P3 evidence for spacing at every Windows scaling factor; it is not represented by inaccessible machine-local paths in this document.

Final result: code structure and UI contracts passed; runtime screenshot evidence remains a follow-up polish item.
