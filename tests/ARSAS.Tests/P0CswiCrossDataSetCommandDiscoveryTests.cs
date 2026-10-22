using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>Structural candidates never grant operate permission. Only live
/// ctlModel + exact CDC/type/semantic proof makes a cross-DataSet CSWI visible.</summary>
public sealed class P0CswiCrossDataSetCommandDiscoveryTests
{
    private static SignalDefinition Control(string path, bool selected = false)
        => new()
        {
            ObjectReference = path,
            Name = path,
            FunctionalConstraint = "CO",
            ControlCdc = "DPC",
            IsControlSignal = true,
            IsSelected = selected,
            ControlModelText = "Auto-detect"
        };

    [Fact]
    public void ControlsInOtherLogicalDevicesAreFoundWithoutDataSetMembership()
    {
        var cbFeedback = Control("GR_X_7SX85CB1/XCBR1.Pos", selected: true);
        cbFeedback.ControlModelText = "Status only";
        var disconnector = Control("GR_X_7SX85Ds1/XSWI1.Pos", selected: true);
        disconnector.ControlModelText = "Status only";
        var cswi = Control("GR_X_7SX85CB1/CSWI1.Pos");
        var other = Control("GR_X_7SX85Ds1/CSWI2.Pos");
        var nameplate = Control("GR_X_7SX85CB1/CSWI1.Mod");
        // Genuinely discoverable CSWI may have no CDC yet; native inspection
        // must establish it, not prefilter this exact DO away as generic.
        other.ControlCdc = "";
        var candidates = Iec61850ControlIdentity.IndependentCswiInspectionCandidates(
            [cbFeedback, disconnector, cswi, other, nameplate]);
        Assert.Equal(2, candidates.Length);
        Assert.Contains(cswi, candidates);
        Assert.Contains(other, candidates);
        Assert.False(cswi.IsSelected);
        Assert.Empty(Iec61850ControlIdentity.VerifiedIndependentCswiControls(candidates));

        // StatusOnly or declaration-only never enables a command.
        cswi.ControlModelText = "Status only";
        Assert.Empty(Iec61850ControlIdentity.VerifiedIndependentCswiControls(candidates));
        cswi.ControlModelText = "SBO • Enhanced security";
        Assert.Empty(Iec61850ControlIdentity.VerifiedIndependentCswiControls(candidates));
        cswi.AcceptVerifiedLiveControlCdc("DPC");
        cswi.ControlSemanticVerified = true;
        Assert.True(cswi.ControlSupportsOperate);
        Assert.Equal(cswi,
            Assert.Single(Iec61850ControlIdentity.VerifiedIndependentCswiControls(candidates)));
        Assert.False(cswi.IsSelected);
        Assert.False(cbFeedback.ControlSupportsOperate);
        Assert.False(disconnector.ControlSupportsOperate);
    }

    [Fact]
    public void AllServiceBackedControlsAreScheduledRegardlessOfNameOrDataset()
    {
        var candidates = Enumerable.Range(1, 2200)
            .Select(i => Control($"ED2_SYS/Prefix{i}GGIO{i}.Command"))
            .ToArray();
        foreach (var item in candidates)
            item.HasControlServiceEvidence = true;
        // Neither a hard-coded LN class nor an arbitrary first-2048 cap is
        // allowed to hide a real service-backed TapChg or vendor control.
        var selected = Iec61850ControlIdentity.DiscoveredControlInspectionCandidates(candidates);
        Assert.Equal(2200, selected.Length);
        Assert.All(selected, item => Assert.False(item.ControlSupportsOperate));
        Assert.Equal(12, Iec61850ControlIdentity.DiscoveredControlInspectionCandidates(
            candidates, budget: 12).Length);
    }

    [Fact]
    public void CrossDataSetCswiProbeIsBoundedDeduplicatedAndDoesNotChangeUserSelection()
    {
        var signals = Enumerable.Range(1, 80)
            .Select(i => Control($"GR_X_7SX85CB1/CSWI{i}.Pos"))
            .ToArray();
        var duplicate = Control(signals[0].ObjectReference);
        var candidates = Iec61850ControlIdentity.IndependentCswiInspectionCandidates(
            signals.Append(duplicate), budget: 24);
        Assert.Equal(24, candidates.Length);
        Assert.Equal(24, candidates.Select(x => x.ObjectReference)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(Iec61850ControlIdentity.IndependentCswiInspectionCandidates(signals, 0));
        Assert.All(signals, x => Assert.False(x.IsSelected));
    }

    [Fact]
    public void CommandDockUsesVerifiedLiveCswiAndPreloadsWithoutMmsWrite()
    {
        string Source(string file)
        {
            for (DirectoryInfo? root = new(AppContext.BaseDirectory);
                 root is not null; root = root.Parent)
            {
                var candidate = Path.Combine(root.FullName,
                    file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
            throw new FileNotFoundException(file);
        }
        var monitor = Source("Models/MonitorModels.cs");
        var preload = Source("MainWindow.CommandPanelUx.cs");
        Assert.Contains("Iec61850ControlIdentity.VerifiedCommandTargets(", monitor);
        Assert.Contains("IsConnected && LiveDiscoveryModel is not null", monitor);
        Assert.Contains("DiscoveredControlInspectionCandidates(device.Signals)", preload);
        Assert.Contains("_runtime.ProbeControlModelAsync(", preload);
        Assert.Contains("device.SelectedControlSignalCount > 0 ||", preload);
        Assert.Contains("device.Signals.Any(", preload);
        Assert.Contains("_runtime.InspectControlAsync(", preload);
        var diagnostic = Source("Services/DiagnosticReportBuilder.cs");
        Assert.Contains("CSWI outside DataSet: live candidates=", diagnostic);
        Assert.Contains("typed-operable=", diagnostic);
        Assert.Contains("operable={cswi.ControlSupportsOperate}", diagnostic);
        Assert.DoesNotContain("ConfigureControlModelAsync(", preload);
        Assert.DoesNotContain("ExecuteControlAsync(", preload);
    }
}
