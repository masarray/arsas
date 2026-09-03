using System.Security.Cryptography;
using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Keeps direct-SCL FAT projection and the Engineering signal catalog on one shared
/// workspace-selection authority. Static DataSet membership remains the strongest identity,
/// while manually selected non-DataSet SCL signals are materialized as persistent FAT rows.
/// FAT TEST/capture scope and FAT disposition are deliberately orthogonal.
/// </summary>
public static class IoFatEngineeringSelectionBridge
{
    public static int Initialize(
        IoTestIedPlan ied,
        Iec61850MonitorDevice device,
        bool preserveExistingEngineeringSelection)
    {
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        var changed = 0;
        foreach (var point in ied.TestPoints.Where(IoTestSignalSelectionService.IsDirectSclAuthority))
        {
            var signal = FindSignal(point, device);
            if (signal is null)
                continue;

            // Shared workspace membership follows the Engineering checkbox. TestEnabled is
            // FAT-only evidence scope and must survive Engineering mode switches unchanged.
            // FatDisposition remains a FAT-only Remove/Restore authority.
            if (preserveExistingEngineeringSelection)
            {
                if (point.WorkspaceSelected != signal.IsSelected)
                {
                    point.WorkspaceSelected = signal.IsSelected;
                    changed++;
                }
            }
            else
            {
                if (!point.WorkspaceSelected)
                {
                    point.WorkspaceSelected = true;
                    changed++;
                }

                if (!signal.IsSelected)
                {
                    signal.IsSelected = true;
                    changed++;
                }
            }
        }

        device.RecountSelectedSignals();
        return changed;
    }

    public static bool ApplyFatPointSelection(
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(device);
        if (!IoTestSignalSelectionService.IsDirectSclAuthority(point))
            return false;

        var signal = FindSignal(point, device);
        if (signal is null)
            return false;

        // Only shared workspace membership is bidirectional. TEST never mutates Engineering,
        // and Remove/Restore never mutates either shared selection or TEST scope.
        var selected = point.WorkspaceSelected;
        if (signal.IsSelected == selected)
            return false;

        signal.IsSelected = selected;
        device.RecountSelectedSignals();
        return true;
    }

    public static bool ApplyEngineeringSignalSelection(
        SignalDefinition signal,
        bool selected,
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        // Engineering owns shared workspace membership only. A FAT row explicitly removed
        // by the operator stays removed, and its FAT TEST preference/evidence is preserved
        // across Engineering deselect/reselect operations.
        var matching = ied.TestPoints
            .Where(IoTestSignalSelectionService.IsDirectSclAuthority)
            .Where(point => ReferenceEquals(FindSignal(point, device), signal))
            .ToArray();

        var changed = false;
        foreach (var point in matching)
        {
            if (point.WorkspaceSelected == selected)
                continue;
            point.WorkspaceSelected = selected;
            changed = true;
        }

        if (matching.Length == 0 && selected && TryCreateManualWorkspacePoint(signal, ied, device, out var createdPoint))
            changed |= ied.AddTestPoint(createdPoint);

        return changed;
    }

    public static SignalDefinition? FindSignal(
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(device);

        var sourceReferences = new[]
            {
                point.SourceIecReference,
                point.ReportDisplayReference,
                point.EventLogSearchReference
            }
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(IoTestLiveBindingService.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (IoTestSignalSelectionService.IsSclDataSetAuthority(point))
        {
            var dataSet = IoTestLiveBindingService.NormalizeReference(point.DataSetName);
            var exactMembership = device.Signals
                .Where(signal => sourceReferences.Contains(
                    IoTestLiveBindingService.NormalizeReference(signal.DisplayReference)))
                .Where(signal => dataSet.Length == 0 ||
                                 dataSet.Equals(
                                     IoTestLiveBindingService.NormalizeReference(signal.DataSetReference),
                                     StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactMembership.Length == 1)
                return exactMembership[0];
        }
        else if (IoTestSignalSelectionService.IsSclWorkspaceAuthority(point))
        {
            var exactSource = device.Signals
                .Where(signal => sourceReferences.Contains(
                    IoTestLiveBindingService.NormalizeReference(signal.DisplayReference)))
                .ToArray();
            if (exactSource.Length == 1)
                return exactSource[0];
        }

        var runtime = IoTestLiveBindingService.NormalizeReference(point.ObjectReference);
        if (runtime.Length == 0)
            return null;

        var runtimeMatches = device.Signals
            .Where(signal => runtime.Equals(
                IoTestLiveBindingService.NormalizeReference(signal.ObjectReference),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return runtimeMatches.Length == 1 ? runtimeMatches[0] : null;
    }

    private static bool TryCreateManualWorkspacePoint(
        SignalDefinition signal,
        IoTestIedPlan ied,
        Iec61850MonitorDevice device,
        out IoTestPointPlan point)
    {
        point = null!;
        if (signal.IsControlSignal || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        var functionalConstraint = signal.FunctionalConstraint?.Trim() ?? string.Empty;
        var dataType = signal.DataType?.Trim() ?? string.Empty;
        var upperType = dataType.ToUpperInvariant();
        var discrete = functionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) &&
                       (upperType.Contains("BOOL", StringComparison.Ordinal) ||
                        upperType.Contains("DBPOS", StringComparison.Ordinal) ||
                        upperType.Contains("SPS", StringComparison.Ordinal) ||
                        upperType.Contains("DPS", StringComparison.Ordinal));
        var analog = functionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase);
        var signalKind = discrete
            ? FatSignalKind.Discrete
            : analog
                ? FatSignalKind.Analog
                : FatSignalKind.Other;
        var captureMode = discrete
            ? FatCaptureMode.AutomaticTransition
            : FatCaptureMode.OperatorSnapshot;
        var displayReference = string.IsNullOrWhiteSpace(signal.DisplayReference)
            ? signal.ObjectReference.Trim()
            : signal.DisplayReference.Trim();
        var runtimeReference = signal.ObjectReference.Trim();
        var sourceSha = device.SclSourceSha256?.Trim().ToLowerInvariant() ?? string.Empty;
        var identity = string.Join("|", new[]
        {
            sourceSha,
            ied.IedName.Trim(),
            device.SclAccessPointName?.Trim() ?? string.Empty,
            displayReference,
            runtimeReference,
            functionalConstraint
        });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();

        point = new IoTestPointPlan
        {
            TestPointId = $"scl-manual-{digest[..20]}",
            IedName = ied.IedName,
            IpAddress = ied.IpAddress,
            SignalName = string.IsNullOrWhiteSpace(signal.Name) ? displayReference : signal.Name.Trim(),
            ObjectReference = runtimeReference,
            FunctionalConstraint = functionalConstraint,
            ExpectedOnText = discrete ? "TRUE" : "Value 1",
            ExpectedOffText = discrete ? "FALSE" : "Value 2",
            ExpectedOnRaw = 1,
            ExpectedOffRaw = 0,
            DataType = dataType,
            SignalAddress = sourceSha,
            DataSetName = signal.DataSetReference?.Trim() ?? string.Empty,
            SourceIecReference = displayReference,
            ReportDisplayReference = displayReference,
            EventLogSearchReference = runtimeReference,
            EvidenceExpected = captureMode == FatCaptureMode.AutomaticTransition
                ? "Automatic Value 1 / Value 2 transition capture"
                : "Operator Value 1 / Value 2 snapshot capture",
            SignalKind = signalKind,
            CaptureMode = captureMode,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
            BindingEvidence = string.Join(" • ", new[]
            {
                "Shared SCL workspace authority",
                $"sourceSha256={sourceSha}",
                $"deviceId={device.DeviceId}",
                $"static={displayReference}",
                $"runtime={runtimeReference}",
                $"fc={functionalConstraint}",
                $"kind={signalKind}",
                $"capture={captureMode}"
            })
        };
        return true;
    }
}
