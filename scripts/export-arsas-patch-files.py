from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
out = root / "docs" / "__arsas_patch_export"
if out.exists():
    shutil.rmtree(out)

files = [
    "MainWindow.xaml",
    "MainWindow.xaml.cs",
    "MainWindow.Demo.cs",
    "MainWindow.DemoSignals.cs",
    "MainWindow.DemoProcessRuntime.cs",
    "MainWindow.DemoGooseData.cs",
    "MainWindow.DemoGooseSnapshot.cs",
    "MainWindow.DemoInteraction.cs",
    "MainWindow.DemoCommandModel.cs",
    "Models/MonitorModels.cs",
    "Models/GooseSubscriberModels.cs",
    "Models/ControlModels.cs",
    "Services/DiagnosticReportBuilder.cs",
    "ArIED61850Tester.csproj",
    "scripts/publish-windows-portable.ps1",
    "scripts/build-windows-installer.ps1",
    "installer/ArIED61850.iss",
    ".github/workflows/build.yml",
    "README.md",
    "landing/index.html",
    "landing/features.html",
    "landing/control.html",
    "landing/architecture.html",
    "landing/site.webmanifest",
]

for relative in files:
    source = root / relative
    target = out / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

print(f"Exported {len(files)} patched files to {out}")
