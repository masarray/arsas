# FAT M2 — Permanent Production Host Verification

M2 removes the FAT launcher as the primary Engineering experience and keeps the proven production FAT surface as the single presentation/engine authority.

## Code contract

- `MainWindow` owns the canonical FAT tab and permanent host slot.
- Engineering static DataSet authority may prewarm FAT before the operator navigates to FAT.
- Prewarm does not change `MainTabs.SelectedIndex`, call `MainWindow.Show()`, call `Activate()`, or steal focus.
- `IoListTestingWindow` remains a temporary compatibility donor/controller owner only; it is prepared transparent, off-screen, non-activating, and absent from the taskbar before `Window.Show()`.
- `MainWindow` remains visible while the embedded donor is initialized.
- The embedded center is the exact production workspace detached from `IoListTestingWindow`; `InstallFatV2WorkspaceUx()` and the existing report-preview installer remain the presentation sources.
- No `Open SCL for FAT` or `Open ARSAS Project` launcher is exposed in the canonical FAT tab.
- Engineering continues to supply the already parsed SCL/static DataSet and shared live runtime; M2 does not introduce a second parser or acquisition authority.

## Verification gate

M2 is **code complete** only when the permanent-host regression tests compile and pass. It is **CI verified** only when the staging head passes the repository Build ARSAS, IO List Testing, and SV evidence validation workflows. Physical no-flicker behavior remains a field-verification item and must not be claimed from CI alone.
