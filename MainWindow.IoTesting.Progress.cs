// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal IoFatPreparationProgressSnapshot GetIoFatPreparationProgressSnapshot(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);

        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        var message = string.IsNullOrWhiteSpace(ied.PreparationStatusText)
            ? $"Preparing {ied.IedName}"
            : ied.PreparationStatusText;
        var requested = ied.TestPoints.Count(point => point.TestEnabled && point.ImportReady);
        var live = ied.TestPoints.Count(point =>
            point.TestEnabled &&
            point.ImportReady &&
            point.LiveBindingState == IoTestLiveBindingState.LivePointReady);
        var liveRatio = requested == 0 ? 0d : Math.Clamp((double)live / requested, 0d, 1d);

        if (device is null)
            return new IoFatPreparationProgressSnapshot(message, 3d, "Step 1 of 10");

        if (device.IsBusy)
        {
            var discovery = Math.Clamp(device.DiscoveryProgressPercent, 0d, 100d) / 100d;
            var refreshing = HasProgressTerm(message, "refreshing live model", "full live discovery", "re-scan");
            var start = refreshing ? 55d : 5d;
            var span = refreshing ? 22d : 52d;
            return new IoFatPreparationProgressSnapshot(
                string.IsNullOrWhiteSpace(device.BusyStage) ? message : device.BusyStage,
                start + discovery * span,
                refreshing ? "Step 5 of 10" : device.DiscoveryProgressStepText);
        }

        if (!device.IsConnected)
            return new IoFatPreparationProgressSnapshot(message, 8d, "Step 2 of 10");

        if (HasProgressTerm(message, "association ready", "reusing the loaded model", "saved endpoint"))
            return new IoFatPreparationProgressSnapshot(message, 60d, "Step 4 of 10");

        if (HasProgressTerm(message, "matching", "workbook signal"))
            return new IoFatPreparationProgressSnapshot(message, 68d, "Step 6 of 10");

        if (HasProgressTerm(message, "refreshing report acquisition", "saved model missed"))
            return new IoFatPreparationProgressSnapshot(message, 75d, "Step 7 of 10");

        if (HasProgressTerm(message, "arming", "report acquisition"))
            return new IoFatPreparationProgressSnapshot(message, 82d, "Step 8 of 10");

        var acquisitionMode = device.AcquisitionMode ?? string.Empty;
        var plannerSettling = HasProgressTerm(
            acquisitionMode,
            "arming",
            "live start",
            "preparing",
            "settling");

        if (device.IsMonitoring)
        {
            var percent = 86d + liveRatio * 12d;
            if (HasProgressTerm(message, "validating", "report coverage", "fallback", "rebuilding"))
                percent = Math.Max(percent, 89d);

            if (requested > 0 && live == requested && !plannerSettling)
                percent = 100d;

            return new IoFatPreparationProgressSnapshot(
                message,
                percent,
                requested > 0 && live == requested ? "Step 10 of 10" : "Step 9 of 10");
        }

        return new IoFatPreparationProgressSnapshot(message, 78d, "Step 7 of 10");
    }

    private static bool HasProgressTerm(string? value, params string[] terms)
    {
        var text = value ?? string.Empty;
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record IoFatPreparationProgressSnapshot(
    string Message,
    double Percent,
    string StepText);
