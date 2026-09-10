from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def write(path, text):
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly 1 match, found {count}")
    return text.replace(old, new, 1)


# M3.1 - Start is a capture-target operation, never a viewed-context operation.
path = "Services/IoTesting/IoTestMultiSessionCoordinator.cs"
s = read(path)
s = replace_once(
    s,
    "        SelectContext(ied);\n        IoTestSessionController controller;",
    "        // M3: the caller latches the capture target before any asynchronous IED\n        // preparation. Starting that target must never steal the Explorer/view context\n        // if the operator selected another IED while preparation was in flight.\n        IoTestSessionController controller;",
    "separate capture Start from viewed SelectContext")
write(path, s)


# M3.2 - Make the embedded Engineering binding contract explicit. SelectedIed is the
# viewed context; per-IED session controllers remain evidence/capture authority.
path = "IoListTestingWindow.EmbeddedEngineeringHost.cs"
s = read(path)
s = replace_once(
    s,
    "        // Do not retarget an active production FAT transaction/session. When idle, the\n        // persistent Engineering IED Explorer is the navigation/selection authority.\n        if (!CanSelectIed)\n            return;",
    "        // M3 viewed-device contract: the persistent Engineering IED Explorer owns\n        // what the FAT grid displays. SelectedIed/Session.SelectContext changes only that\n        // projection; an active capture remains latched inside its per-IED controller.\n        if (!CanSelectIed)\n            return;",
    "document viewed-device binding")
write(path, s)


# M3.3 - Document the async latch at the exact point where it is established.
path = "IoListTestingWindow.xaml.cs"
s = read(path)
s = replace_once(
    s,
    "        var selectedIed = SelectedIed;\n        if (selectedIed?.IsPreparing == true)",
    "        // Capture target latch: keep this local IED for the complete async prepare +\n        // Start transaction even if the global Engineering Explorer changes selection.\n        var selectedIed = SelectedIed;\n        if (selectedIed?.IsPreparing == true)",
    "document async capture target latch")
write(path, s)

print("M3 viewed-device / capture-target separation applied successfully")
