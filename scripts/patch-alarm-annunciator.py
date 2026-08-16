from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)

# Remove dependency on a theme geometry that does not exist yet. Keep the bell local
# to this empty-state illustration so the feature does not broaden global theme surface.
xaml_path = Path("MainWindow.xaml")
xaml = xaml_path.read_text(encoding="utf-8")
xaml = replace_once(
    xaml,
    '<Path Data="{StaticResource LucideBell}" Style="{StaticResource LucideIcon}" Stroke="#2563EB"/>',
    '<Path Data="M18,8 A6,6 0 0 0 6,8 C6,15 3,17 3,17 H21 C21,17 18,15 18,8 M10,21 H14" Style="{StaticResource LucideIcon}" Stroke="#2563EB"/>',
    "inline bell vector")
xaml_path.write_text(xaml, encoding="utf-8", newline="\n")

# Keep configured windows visible when live points temporarily disappear (stop monitor,
# reconnect, project restore). This preserves operator acknowledgement state while making
# the data-source availability explicit.
controller_path = Path("MainWindow.AlarmAnnunciator.cs")
controller = controller_path.read_text(encoding="utf-8")
old_sync = '''    private void SynchronizeAnnunciatorPointSelection()\n    {\n        foreach (var point in GlobalPoints)\n        {\n            var configured = point.CanUseAsAnnunciator && IsAnnunciatorConfigured(point.DeviceId, point.IecReference);\n            point.IsAnnunciatorSelected = configured;\n            if (configured)\n                EnsureAnnunciatorItem(point).InitializeFromPoint(point);\n        }\n        RaiseAnnunciatorSummary();\n    }'''
new_sync = '''    private void SynchronizeAnnunciatorPointSelection()\n    {\n        var livePointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n        foreach (var point in GlobalPoints)\n        {\n            livePointKeys.Add(point.PointKey);\n            var configured = point.CanUseAsAnnunciator && IsAnnunciatorConfigured(point.DeviceId, point.IecReference);\n            point.IsAnnunciatorSelected = configured;\n            if (configured)\n                EnsureAnnunciatorItem(point).InitializeFromPoint(point);\n        }\n\n        foreach (var item in AnnunciatorAlarms.Where(item => !livePointKeys.Contains(item.PointKey)))\n            item.MarkUnavailable("Offline / waiting for live point");\n\n        RaiseAnnunciatorSummary();\n    }'''
controller = replace_once(controller, old_sync, new_sync, "offline annunciator preservation")
controller_path.write_text(controller, encoding="utf-8", newline="\n")

nav_path = Path("MainWindow.NavigationLayoutFix.cs")
nav = nav_path.read_text(encoding="utf-8")
nav = nav.replace("five equal star columns", "six equal star columns")
nav = nav.replace("live connection/status chips are also present", "workspace controls are also present")
nav_path.write_text(nav, encoding="utf-8", newline="\n")

print("Alarm Annunciator hardening patch applied.")
