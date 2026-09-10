from pathlib import Path

path = Path(__file__).with_name("fat_m2_permanent_host.py")
source = path.read_text(encoding="utf-8")
old = '''s = replace_once(\n    s,\n    "        _nativeFatTab.Content = BuildProductionFatLauncher();",\n    "        _nativeFatTab.Content = BuildProductionFatPermanentHost();\\n\\n        // M2 prewarm starts as soon as the canonical host exists. It is deliberately\\n        // independent of the currently selected tab so a valid Engineering SCL/DataSet\\n        // can prepare the exact production surface before the operator first opens FAT.\\n        QueueProductionFatEngineeringBootstrap();",\n    "install permanent FAT host")'''
new = '''install_launcher = "        _nativeFatTab.Content = BuildProductionFatLauncher();"\nif s.count(install_launcher) != 2:\n    raise SystemExit(f"install permanent FAT host: expected install + unmount matches, found {s.count(install_launcher)}")\ns = s.replace(\n    install_launcher,\n    "        _nativeFatTab.Content = BuildProductionFatPermanentHost();\\n\\n        // M2 prewarm starts as soon as the canonical host exists. It is deliberately\\n        // independent of the currently selected tab so a valid Engineering SCL/DataSet\\n        // can prepare the exact production surface before the operator first opens FAT.\\n        QueueProductionFatEngineeringBootstrap();",\n    1)'''
if source.count(old) != 1:
    raise SystemExit(f"guard fixer expected one original install block, found {source.count(old)}")
path.write_text(source.replace(old, new, 1), encoding="utf-8")
print("M2 guard corrected: launcher install/unmount pair distinguished")
