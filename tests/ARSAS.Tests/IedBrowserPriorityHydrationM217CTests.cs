using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Full-discovery priority hydration consumes existing ARIEC serial MMS Read
/// rather than creating a second acquisition owner or reading every FC.
/// </summary>
public sealed class IedBrowserPriorityHydrationM217CTests
{
    private static LiveIedDataObjectModel Obj(
        string ln, string name, string fc, string cdc, params string[] leaves)
    {
        var owner = $"LD/{ln}.{name}";
        var dot = owner.IndexOf('.');
        return new LiveIedDataObjectModel
        {
            Name = name, Reference = owner, InferredCdc = cdc,
            Attributes = leaves.Select(leaf => new LiveIedDataAttributeModel
            {
                ObjectReference = owner,
                AttributePath = name + "." + leaf,
                MmsReference = owner[..dot] + "$" + fc + "$" + name +
                               "$" + leaf.Replace('.', '$'),
                FunctionalConstraint = fc,
                SclBType = leaf is "q" ? "Quality" : leaf is "t" ? "Timestamp" :
                    cdc == "DPC" ? "Dbpos" : "Enum",
                TypeSource = "LiveTypeSpecification"
            }).ToArray()
        };
    }

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataObjectModel[] objects)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel { DataObjects = objects }]
            }]
        };

    private static string Canonical(IedBrowserReadTarget target)
        => IedBrowserLiveValues.CanonicalReference(target.Reference);

    [Fact]
    public void PriorityIsStationHealthThenTypedSwitchThenNameplateThenGeneralStatus()
    {
        var model = Model(
            Obj("GGIO1", "AnIn1", "MX", "MV", "mag.f"),
            Obj("GGIO1", "Beh", "ST", "ENS", "stVal"),
            Obj("LLN0", "NamPlt", "DC", "LPL", "vendor", "swRev"),
            Obj("XCBR1", "Pos", "ST", "DPC", "stVal", "q", "t"),
            Obj("LPHD1", "Health", "ST", "ENS", "stVal"),
            Obj("LLN0", "Mod", "ST", "INC", "stVal"),
            Obj("CSWI1", "Pos", "CO", "DPC", "ctlVal"));
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var targets = IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32);
        Assert.Equal(8, targets.Count);
        Assert.Equal("LD/LLN0.Mod.stVal", Canonical(targets[0]));
        Assert.Equal("LD/LPHD1.Health.stVal", Canonical(targets[1]));
        Assert.Equal("LD/XCBR1.Pos.stVal", Canonical(targets[2]));
        Assert.Equal("LD/LLN0.NamPlt.swRev", Canonical(targets[3]));
        Assert.Equal("LD/LLN0.NamPlt.vendor", Canonical(targets[4]));
        Assert.Equal("LD/GGIO1.Beh.stVal", Canonical(targets[5]));
        Assert.Equal("LD/XCBR1.Pos.q", Canonical(targets[6]));
        Assert.Equal("LD/XCBR1.Pos.t", Canonical(targets[7]));
        Assert.DoesNotContain(targets, t => t.FunctionalConstraint == "CO" ||
            Canonical(t).Contains("AnIn", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingVerifiedReadOrReportPreventsRedundantInitialMmsRead()
    {
        var model = Model(Obj("LLN0", "Mod", "ST", "INC", "stVal"),
            Obj("LLN0", "Health", "ST", "ENS", "stVal"));
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            "LD/LLN0.Mod.stVal", "ST", "1", "", "", "Report"));
        Assert.Single(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 16));
        Assert.Equal("LD/LLN0.Health.stVal", Canonical(
            IedBrowserReadPlan.BuildInitialPriority(model, ledger, 16)[0]));
        // Changing the only evidence's association invalidates the old
        // snapshot; it must NOT satisfy a new live hydration.
        ledger.BeginAssociation();
        Assert.Equal(2, IedBrowserReadPlan.BuildInitialPriority(model, ledger, 16).Count);
        ledger.MarkOffline();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 16));
    }

    [Fact]
    public void PlannerFailsClosedOnDuplicateIdentityAndWrongFcAndCdc()
    {
        var duplicate = Obj("LLN0", "Mod", "ST", "INC", "stVal");
        var model = Model(duplicate,
            Obj("LLN0", "Mod", "ST", "INC", "stVal"),
            Obj("XCBR1", "Pos", "ST", "SPS", "stVal"),
            Obj("XCBR2", "Pos", "MX", "DPC", "stVal"),
            Obj("XCBR3", "Pos", "CO", "DPC", "ctlVal"),
            Obj("GEN1", "Thing", "ST", "DPC", "stVal"));
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32));
    }

    [Fact]
    public void TargetBudgetIsDeterministicAndHardBoundedForLargeIED()
    {
        var objects = Enumerable.Range(1, 400)
            .Select(i => Obj("GGIO" + i, "Beh", "ST", "ENS", "stVal"))
            .ToArray();
        var model = Model(objects);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var a = IedBrowserReadPlan.BuildInitialPriority(model, ledger, 24);
        var b = IedBrowserReadPlan.BuildInitialPriority(model, ledger, 24);
        Assert.Equal(24, a.Count);
        Assert.Equal(a.Select(x => x.Reference), b.Select(x => x.Reference));
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 0));
    }

    [Fact]
    public void InitialFailureIsModelAndAssociationBoundNotUiCatalogBound()
    {
        var model = Model(Obj("LLN0", "Health", "ST", "ENS", "stVal"));
        var device = new Iec61850MonitorDevice { LiveDiscoveryModel = model, IsConnected = true };
        var target = IedBrowserReadPlan.BuildInitialPriority(model, device.LiveValues, 3)[0];
        var failures = new IedBrowserReadFailureEvidence(device, bindToLiveModel: true);
        failures.RecordFailure(target, "MMS data-access-error: object-non-existent");
        var reference = Canonical(target);
        Assert.True(failures.TryGet(device, reference, "", "ST", out var reason));
        Assert.Contains("object-non-existent", reason);
        device.Signals.ReplaceAll([new SignalDefinition
        {
            ObjectReference = reference, FunctionalConstraint = "ST"
        }]);
        Assert.True(failures.TryGet(device, reference, "", "ST", out _));
        Assert.False(failures.TryGet(device, reference, "", "MX", out _));
        Assert.False(failures.TryGet(device, reference.ToLowerInvariant(), "", "ST", out _));
        device.LiveDiscoveryModel = Model(Obj("LLN0", "Health", "ST", "ENS", "stVal"));
        Assert.False(failures.TryGet(device, reference, "", "ST", out _));
        device.LiveDiscoveryModel = model;
        device.IsConnected = false;
        device.IsConnected = true;
        Assert.False(failures.TryGet(device, reference, "", "ST", out _));
    }

    [Fact]
    public void ManualReadFailureStillFencesCollectionEpochAndNeverAuthorizesValues()
    {
        var model = Model(Obj("LLN0", "Health", "ST", "ENS", "stVal"));
        var device = new Iec61850MonitorDevice { LiveDiscoveryModel = model, IsConnected = true };
        var failure = new IedBrowserReadFailureEvidence(device);
        var target = IedBrowserReadPlan.BuildInitialPriority(model, device.LiveValues, 1)[0];
        failure.RecordFailure(target, "unsupported");
        Assert.True(failure.TryGet(device, Canonical(target), "", "ST", out _));
        device.Signals.ReplaceAll([new SignalDefinition
        {
            ObjectReference = Canonical(target), FunctionalConstraint = "ST"
        }]);
        Assert.False(failure.TryGet(device, Canonical(target), "", "ST", out _));
        Assert.False(device.LiveValues.TryGet(Canonical(target), "ST", out _));
    }

    [Fact]
    public void ReattachedExplorerSignalsMirrorExistingReadEvidenceExactlyOnce()
    {
        var model = Model(Obj("LLN0", "Health", "ST", "ENS", "stVal"));
        var device = new Iec61850MonitorDevice { LiveDiscoveryModel = model, IsConnected = true };
        var reference = "LD/LLN0.Health.stVal";
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            reference, "ST", "2", "", "", "MMS Read"));
        var signal = new SignalDefinition
        {
            ObjectReference = reference, FunctionalConstraint = "ST"
        };
        device.Signals.ReplaceAll([signal]);
        var index = new IedBrowserReadBindingIndex(device);
        Assert.Equal(1, index.ProjectCurrentReadSignals(device.LiveValues, association));
        Assert.Equal("2", signal.Value);
        Assert.Equal("Unknown", signal.Quality);
        Assert.Equal("-", signal.DeviceTimestamp);
        Assert.Equal(CanonicalSignalObservationState.LiveRead, signal.ObservationState);
        Assert.Equal(association, signal.ObservationAssociation);
        Assert.False(device.LiveValues.TryGet(reference, "MX", out _));

        // Replaced UI catalog cannot receive queued old projections.
        var replacement = new SignalDefinition
        {
            ObjectReference = reference, FunctionalConstraint = "ST"
        };
        device.Signals.ReplaceAll([replacement]);
        Assert.Equal(0, index.ProjectCurrentReadSignals(device.LiveValues, association));
        Assert.Equal(1, new IedBrowserReadBindingIndex(device)
            .ProjectCurrentReadSignals(device.LiveValues, association));
        Assert.Equal("2", replacement.Value);
        device.IsMonitoring = true;
        Assert.Equal(0, new IedBrowserReadBindingIndex(device)
            .ProjectCurrentReadSignals(device.LiveValues, association));
        device.IsConnected = false;
        Assert.Equal(0, new IedBrowserReadBindingIndex(device)
            .ProjectCurrentReadSignals(device.LiveValues, association));
    }

    [Fact]
    public void RuntimeHydrationIsSerialBudgetedAndUsesCurrentLedgerWithoutControlWrites()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "Services",
                   "Iec61850MonitorRuntime.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Services",
            "Iec61850MonitorRuntime.cs"));
        Assert.Contains("InitialHydrationMaxTargets = 32", src);
        Assert.Contains("InitialHydrationTotalBudget = TimeSpan.FromSeconds(10)", src);
        Assert.Contains("await ReadBrowserValuesAsync(device, plan, inbox,", src);
        Assert.Contains("new IedBrowserReadResultInbox(static () => { })", src);
        Assert.Contains("device.LiveValues.PublishForAssociation(association,", src);
        Assert.Contains("device.IsMonitoring", src);
        Assert.Contains("ReferenceEquals(device.LiveDiscoveryModel, model)", src);
        var ui = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("device.InitialHydrationFailures?.TryGet(", ui);
        var main = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("ProjectCurrentReadSignals(", main);
    }
}
