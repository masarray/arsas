using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Routine Command Dock refresh must obey the same MMS association and model
/// epoch ownership as the dedicated post-Write reinspection. These tests use
/// deterministic state changes and verify the actual WPF/native dispatch seam,
/// without simulating a real IEC 61850 Operate.
/// </summary>
public sealed class M217P0RefreshControlModelEpochTests
{
    [Fact]
    public void RefreshTargetFenceRejectsSameSocketReplacedModelAndRetiredRows()
    {
        var expectedMms = Guid.NewGuid();
        var currentMms = expectedMms;
        const long expectedModel = 17;
        long liveModel = expectedModel;
        var deviceConnected = true;
        var original = new SignalDefinition
        {
            Name = "CSWI1 Pos",
            ObjectReference = "LD0/CSWI1.Pos",
            FunctionalConstraint = "CO",
            IsControlSignal = true
        };
        var replacement = new SignalDefinition
        {
            Name = "CSWI1 Pos (rebound)",
            ObjectReference = original.ObjectReference,
            FunctionalConstraint = "CO",
            IsControlSignal = true
        };
        var live = new List<SignalDefinition> { original };
        bool IsCurrent(SignalDefinition candidate) =>
            Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
                expectedMms, id => id == currentMms,
                expectedModel, liveModel, deviceConnected, live.Contains(candidate));

        Assert.True(IsCurrent(original));
        Assert.False(IsCurrent(replacement));

        // The same MMS GUID does not make a rescan's replacement data model
        // an authorized recipient of the previous asynchronous inspection.
        live.Clear();
        live.Add(replacement);
        Assert.False(IsCurrent(original));
        Assert.False(IsCurrent(original)); // duplicate-root names never grant ownership
        liveModel++;
        Assert.False(IsCurrent(replacement));

        liveModel = expectedModel;
        live.Clear();
        live.Add(original);
        currentMms = Guid.NewGuid();
        Assert.False(IsCurrent(original));
        currentMms = expectedMms;
        deviceConnected = false;
        Assert.False(IsCurrent(original));
    }

    [Fact]
    public void RoutineRefreshPassesModelFenceToFacadeBeforeGuiMutationAndFailure()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(
                   Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));
        var begin = src.IndexOf("private async Task RefreshControlValuesAsync(",
            StringComparison.Ordinal);
        var end = src.IndexOf("private bool RejectCommandWhenIedFlightUnavailable(",
            begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var refresh = src[begin..end];

        Assert.Contains("expectedMmsAssociation = _runtime.GetControlAssociationId(device.DeviceId)",
            refresh);
        Assert.Contains("expectedModelAssociation = device.LiveValues.Association", refresh);
        Assert.Contains("Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(",
            refresh);
        Assert.Contains("device.Signals.Contains(candidate)", refresh);
        Assert.Contains("() => IsCurrentRefreshTarget(signal));", refresh);
        Assert.Contains("if (!IsCurrentRefreshTarget(signal))", refresh);
        Assert.Contains("if (!IsCurrentRefreshDevice())", refresh);
        Assert.Equal(1, refresh.Split("_runtime.InspectControlAsync(").Length - 1);
        Assert.DoesNotContain("session.Client.InspectControlAsync(", refresh);

        var awaitEnd = refresh.IndexOf("() => IsCurrentRefreshTarget(signal));",
            StringComparison.Ordinal);
        var mutation = refresh.IndexOf("signal.ControlCurrentValue = capabilities.CurrentValue",
            StringComparison.Ordinal);
        Assert.True(awaitEnd >= 0 && mutation > awaitEnd);
        var genericCatch = refresh.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var faultGuard = refresh.IndexOf("if (!IsCurrentRefreshTarget(signal))",
            genericCatch, StringComparison.Ordinal);
        var faultMutation = refresh.IndexOf("signal.ControlInspectionEvidence =",
            genericCatch, StringComparison.Ordinal);
        Assert.True(genericCatch >= 0 && faultGuard > genericCatch &&
                    faultMutation > faultGuard);
        var projection = refresh.IndexOf("device.RefreshCommandSignalProjection()",
            StringComparison.Ordinal);
        var projectGuard = refresh.LastIndexOf("if (!IsCurrentRefreshDevice())",
            projection, StringComparison.Ordinal);
        Assert.True(projectGuard >= 0 && projection > projectGuard);
    }
}
