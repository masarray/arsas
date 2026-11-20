using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217DControlReadFailureDiagnosticsTests
{
    private static LiveIedControlBlockModel Block(string kind = "SettingGroupControl",
        string fc = "SP", string name = "SGCB", string status = "AttributeInventoryOnly")
        => new()
        {
            Domain = "LD", LogicalNode = "LLN0", Name = name,
            Kind = kind, FunctionalConstraint = fc,
            DiscoveryStatus = status,
            Reference = $"LD/LLN0.{fc}.{name}",
            Attributes = kind == "SettingGroupControl"
                ? ["NumOfSG", "ActSG", "LActTm", "EditSG"]
                : ["GoEna", "NdsCom"]
        };

    private static (Iec61850MonitorDevice Device, IedBrowserReadFailureEvidence Initial)
        Connected()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }]
        };
        var device = new Iec61850MonitorDevice
        {
            LiveDiscoveryModel = model, IsConnected = true
        };
        return (device, new IedBrowserReadFailureEvidence(
            device, bindToLiveModel: true));
    }

    [Fact]
    public void VerifiedSgcbInitialReadErrorIsSurfacedWithoutInventingValue()
    {
        var (device, initial) = Connected();
        var block = Block();
        initial.RecordFailure(new IedBrowserReadTarget(
            "LD/LLN0$SP$SGCB$ActSG", "SP", "UInt32"), "data-access-error: object-non-existent");

        Assert.True(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "SGCB", "ActSG", null, initial,
            out var reason, out var origin, out var reference));
        Assert.Equal("MMS initial Read failed", origin);
        Assert.Equal("LD/LLN0.SGCB.ActSG", reference);
        Assert.Contains("object-non-existent", reason);
        Assert.False(device.LiveValues.TryGet(reference, "SP", out _));
        Assert.False(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "SGCB", "EditSG", null, initial, out _, out _, out _));
        Assert.False(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "SGCB", "ActSG", null, initial, out _, out _, out _)
            && device.LiveValues.IsOffline);
    }

    [Fact]
    public void ManualFailureTakesPrecedenceThenFallsBackAfterCatalogReplacement()
    {
        var (device, initial) = Connected();
        var block = Block();
        var target = new IedBrowserReadTarget(
            "LD/LLN0.SGCB.NumOfSG", "SP", "UInt32");
        initial.RecordFailure(target, "initial denied");
        var manual = new IedBrowserReadFailureEvidence(device);
        manual.RecordFailure(target, "manual timeout");
        Assert.True(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "SGCB", "NumOfSG", manual, initial,
            out var reason, out var origin, out _));
        Assert.Equal("manual timeout", reason);
        Assert.Equal("MMS manual Read failed", origin);
        device.Signals.ReplaceAll([new SignalDefinition
        {
            ObjectReference = "LD/LLN0.Mod.stVal", FunctionalConstraint = "ST"
        }]);
        Assert.True(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "SGCB", "NumOfSG", manual, initial,
            out reason, out origin, out _));
        Assert.Equal("initial denied", reason);
        Assert.Equal("MMS initial Read failed", origin);
    }

    [Fact]
    public void GoCbReadFailureIsNotGooseFrameEvidenceOrAnSclOnlyAssumption()
    {
        var (device, initial) = Connected();
        var block = Block(kind: "GSEControl", fc: "GO", name: "BCU");
        initial.RecordFailure(new IedBrowserReadTarget(
            "LD/LLN0.BCU.GoEna", "GO", "Boolean"), "MMS reject");
        Assert.True(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "GoCB", "GoEna", null, initial,
            out _, out var source, out _));
        Assert.Equal("MMS initial Read failed", source);
        Assert.False(IedBrowserControlStateEvidence.TryReadFailure(device,
            block, "GoCB", "NdsCom", null, initial, out _, out _, out _));
        Assert.False(IedBrowserControlStateEvidence.TryReadFailure(device,
            Block(kind: "GSEControl", fc: "GO", name: "BCU", status: "SCLOnly"),
            "GoCB", "GoEna", null, initial, out _, out _, out _));
    }

    [Fact]
    public void FailureDiagnosticSampleIsBoundedSortedAndOnlyCurrentAssociation()
    {
        var (device, initial) = Connected();
        for (var i = 40; i >= 1; i--)
            initial.RecordFailure(new IedBrowserReadTarget(
                $"LD/LLN0.Mod{i:000}.stVal", "ST", "Enum"),
                "read error " + i);
        var sample = initial.SnapshotCurrent(device, maxEntries: 200);
        Assert.Equal(32, sample.Count);
        Assert.Equal("LD/LLN0.Mod001.stVal", sample[0].Reference);
        Assert.All(sample, s => Assert.Equal("ST", s.FunctionalConstraint));
        Assert.Empty(initial.SnapshotCurrent(device, maxEntries: 0));
        var (other, _) = Connected();
        Assert.Empty(initial.SnapshotCurrent(other));
        device.IsConnected = false;
        Assert.Empty(initial.SnapshotCurrent(device));
        device.IsConnected = true;
        Assert.Empty(initial.SnapshotCurrent(device));
    }

    [Fact]
    public void DiagnosticsUseExistingFailureLedgerWithoutNetworkReadOrShadowCache()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null &&
               !File.Exists(Path.Combine(folder.FullName, "Services", "DiagnosticReportBuilder.cs")))
            folder = folder.Parent;
        Assert.NotNull(folder);
        var report = File.ReadAllText(Path.Combine(folder!.FullName,
            "Services", "DiagnosticReportBuilder.cs"));
        var browser = File.ReadAllText(Path.Combine(folder.FullName,
            "MainWindow.IedBrowser.cs"));
        Assert.Contains("SnapshotCurrent(device, maxEntries: 12)", report);
        Assert.Contains("Browser ledger   : association=", report);
        Assert.Contains("Initial MMS errors (sample max 12)", report);
        Assert.Contains("device.InitialHydrationFailures,", browser);
        Assert.Contains("IedBrowserControlStateEvidence.TryReadFailure(", browser);
    }
}
