from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "IoListTestingWindow.EmbeddedEngineeringHost.cs"
source = path.read_text(encoding="utf-8")

old = '''    internal void SelectEngineeringDeviceForEmbeddedFat(Iec61850MonitorDevice? device)
    {
        if (!_engineeringEmbeddedMounted || device == null)
            return;

        var match = Project.Ieds.FirstOrDefault(ied =>
            (!string.IsNullOrWhiteSpace(ied.LiveDeviceId) &&
             ied.LiveDeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(ied.IpAddress) &&
             ied.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)) ||
            ied.IedName.Equals(device.SclIedName, StringComparison.OrdinalIgnoreCase) ||
            ied.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
        if (match == null || ReferenceEquals(SelectedIed, match))
            return;

        // M3 viewed-device contract: the persistent Engineering IED Explorer owns
        // what the FAT grid displays. SelectedIed/Session.SelectContext changes only that
        // projection; an active capture remains latched inside its per-IED controller.
        if (!CanSelectIed)
            return;

        SelectedIed = match;
    }
'''

new = '''    internal void SelectEngineeringDeviceForEmbeddedFat(Iec61850MonitorDevice? device)
    {
        if (!_engineeringEmbeddedMounted)
            return;

        // M3 viewed-device contract: the persistent Engineering IED Explorer owns
        // what the FAT grid displays. SelectedIed/Session.SelectContext changes only that
        // projection; an active capture remains latched inside its per-IED controller.
        // Clearing the Explorer selection or selecting an IED outside this FAT projection
        // must clear the FAT view instead of silently leaving the previous IED on screen.
        if (device == null)
        {
            if (CanSelectIed)
                SelectedIed = null;
            return;
        }

        // Resolve by strongest Engineering identity first. Do not use one OR predicate:
        // a weak fallback on an earlier project row must never beat an exact live DeviceId.
        IoTestIedPlan? match = null;
        if (!string.IsNullOrWhiteSpace(device.DeviceId))
        {
            match = Project.Ieds.FirstOrDefault(ied =>
                !string.IsNullOrWhiteSpace(ied.LiveDeviceId) &&
                ied.LiveDeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (match == null && !string.IsNullOrWhiteSpace(device.SclIedName))
        {
            match = Project.Ieds.FirstOrDefault(ied =>
                ied.IedName.Equals(device.SclIedName, StringComparison.OrdinalIgnoreCase));
        }

        if (match == null && !string.IsNullOrWhiteSpace(device.Name))
        {
            match = Project.Ieds.FirstOrDefault(ied =>
                ied.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (match == null && !string.IsNullOrWhiteSpace(device.IpAddress))
        {
            match = Project.Ieds.FirstOrDefault(ied =>
                !string.IsNullOrWhiteSpace(ied.IpAddress) &&
                ied.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase));
        }

        if (ReferenceEquals(SelectedIed, match) || !CanSelectIed)
            return;

        SelectedIed = match;
    }
'''

count = source.count(old)
if count != 1:
    raise SystemExit(f"M3 view binding guard: expected exactly one old method, found {count}")

path.write_text(source.replace(old, new, 1), encoding="utf-8")
print("M3 deterministic Engineering viewed-IED binding applied")
