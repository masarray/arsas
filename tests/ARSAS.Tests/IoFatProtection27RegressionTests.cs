using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatProtection27RegressionTests
{
    private readonly IoTestSignalSelectionService _selection = new();
    private readonly IoTestLiveBindingService _binding = new();

    [Fact]
    public void SevenSx80Protection27_CompleteWorkbookReference_MatchesCorrectPtrcAmongSiblingFunctions()
    {
        var point = CompleteProtection27Point(
            "UCC-IEC-0041",
            "Protection operated (27)",
            "AA1C1F03R3Application/VI3p1_27Undervoltage1/PTRC1.Op.general",
            "VI3p1_27Undervoltage1/PTRC1.Op");
        var ied = Ied("AA1C1F03R3", "192.168.81.69", point);
        var device = Device(
            "AA1C1F03R3",
            "192.168.81.69",
            Signal("Protection operated (67)", "AA1C1F03R3VI3p1_67DirOC3phB1/PTRC1$ST$Op$general"),
            Signal("Protection operated (59)", "AA1C1F03R3VI3p1_59Overvoltage1/PTRC1$ST$Op$general"),
            Signal("Protection operated (27)", "AA1C1F03R3VI3p1_27Undervoltage1/PTRC1$ST$Op$general"));

        var result = _selection.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        var match = Assert.Single(result.Matches);
        Assert.Equal(
            "AA1C1F03R3VI3p1_27Undervoltage1/PTRC1$ST$Op$general",
            match.Signal.ObjectReference);
        // Event-log reference + DA reconstructs the exact MMS identity, so this
        // intentionally wins at ExactScore rather than being reported as a normalized fallback.
        Assert.False(match.UsedNormalizedIedPrefix);
    }

    [Fact]
    public void SevenSx80Protection27_ApplicationFolderVariant_MatchesConcatenatedLiveLnForm()
    {
        var point = CompleteProtection27Point(
            "UCC-IEC-0041",
            "Protection operated (27)",
            "AA1C1F03R3Application/VI3p1_27Undervoltage1/PTRC1.Op.general",
            "VI3p1_27Undervoltage1/PTRC1.Op");
        var ied = Ied("AA1C1F03R3", "192.168.81.69", point);
        var device = Device(
            "AA1C1F03R3",
            "192.168.81.69",
            Signal(
                "Protection operated (27)",
                "AA1C1F03R3Application/VI3p1_27Undervoltage1PTRC1$ST$Op$general"));

        var result = _selection.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void SevenSx80Protection27_Weak27FirstRow_UsesStrongSiblingAsSafeEliminationEvidence()
    {
        // Field case: 27-1 lost its LD/LN path and arrived as `.Op.general`, while
        // 27-2 still retained its full VI1p1_27Undervoltage1/PTRC1 reference. Keep the
        // weak row first to prove matching is no longer dependent on workbook row order.
        var weak27First = WeakProtection27Point(
            "UCC-IEC-0733",
            "Protection operated (27-1)");
        var strong27Second = CompleteProtection27Point(
            "UCC-IEC-0734",
            "Protection operated (27-2)",
            "AA1C1F13R1Application/VI1p1_27Undervoltage1/PTRC1.Op.general",
            "VI1p1_27Undervoltage1/PTRC1.Op");
        var ied = Ied("AA1C1F13R1", "192.168.81.14", weak27First, strong27Second);
        var device = Device(
            "AA1C1F13R1",
            "192.168.81.14",
            Signal("Protection operated (51)", "AA1C1F13R1VI3p1_5051OC3phase1/II_PTOC1$ST$Op$general"),
            Signal("Protection operated (27-1)", "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1$ST$Op$general"),
            Signal("Protection operated (27-2)", "AA1C1F13R1VI1p1_27Undervoltage1/PTRC1$ST$Op$general"));

        var result = _selection.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(
            "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1$ST$Op$general",
            result.Matches.Single(match => ReferenceEquals(match.TestPoint, weak27First)).Signal.ObjectReference);
        Assert.Equal(
            "AA1C1F13R1VI1p1_27Undervoltage1/PTRC1$ST$Op$general",
            result.Matches.Single(match => ReferenceEquals(match.TestPoint, strong27Second)).Signal.ObjectReference);
    }

    [Fact]
    public void SevenSx80Protection27_WeakReferenceWithoutSiblingProof_RemainsAmbiguous()
    {
        var weak27 = WeakProtection27Point(
            "UCC-IEC-0733",
            "Protection operated (27-1)");
        var ied = Ied("AA1C1F13R1", "192.168.81.14", weak27);
        var device = Device(
            "AA1C1F13R1",
            "192.168.81.14",
            Signal("Protection operated (27 A)", "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1$ST$Op$general"),
            Signal("Protection operated (27 B)", "AA1C1F13R1VI1p1_27Undervoltage1/PTRC1$ST$Op$general"));

        var result = _selection.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Empty(result.MissingPoints);
        Assert.Single(result.AmbiguousPoints);
    }

    [Fact]
    public void PreparedProtection27Reference_RemainsAuthoritativeDuringLiveBinding()
    {
        var weak27First = WeakProtection27Point(
            "UCC-IEC-0733",
            "Protection operated (27-1)");
        var strong27Second = CompleteProtection27Point(
            "UCC-IEC-0734",
            "Protection operated (27-2)",
            "AA1C1F13R1Application/VI1p1_27Undervoltage1/PTRC1.Op.general",
            "VI1p1_27Undervoltage1/PTRC1.Op");
        var ied = Ied("AA1C1F13R1", "192.168.81.14", weak27First, strong27Second);
        var project = new IoTestProject
        {
            ProjectId = "CCPP-P0-27",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "Protection 27 regression",
            Ieds = { ied }
        };
        project.InitializeRuntimeNotifications();
        var device = Device(
            "AA1C1F13R1",
            "192.168.81.14",
            Signal("Protection operated (51)", "AA1C1F13R1VI3p1_5051OC3phase1/II_PTOC1$ST$Op$general"),
            Signal("Protection operated (27-1)", "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1$ST$Op$general"),
            Signal("Protection operated (27-2)", "AA1C1F13R1VI1p1_27Undervoltage1/PTRC1$ST$Op$general"));

        var selection = _selection.Resolve(ied, device);
        Assert.True(selection.Succeeded, selection.Message);
        foreach (var match in selection.Matches)
        {
            match.TestPoint.ApplyLiveBinding(
                IoTestLiveBindingState.BoundNormalized,
                "Prepared by regression test",
                device.DeviceId,
                match.Signal.ObjectReference);
        }

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(2, summary.SignalBoundCount);
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(
            "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1$ST$Op$general",
            weak27First.LiveSignalReference);
        Assert.True(weak27First.IsLiveBound);
    }

    private static IoTestPointPlan CompleteProtection27Point(
        string id,
        string signalName,
        string objectReference,
        string eventReference) => new()
    {
        TestPointId = id,
        IedName = objectReference.StartsWith("AA1C1F13R1", StringComparison.OrdinalIgnoreCase)
            ? "AA1C1F13R1"
            : "AA1C1F03R3",
        IpAddress = objectReference.StartsWith("AA1C1F13R1", StringComparison.OrdinalIgnoreCase)
            ? "192.168.81.14"
            : "192.168.81.69",
        SignalName = signalName,
        ObjectReference = objectReference,
        LogicalDevice = objectReference.StartsWith("AA1C1F13R1", StringComparison.OrdinalIgnoreCase)
            ? "AA1C1F13R1Application"
            : "AA1C1F03R3Application",
        LogicalNode = "PTRC1",
        DataObject = "Op",
        DataAttribute = "general",
        FunctionalConstraint = "ST",
        EventLogSearchReference = eventReference,
        SourceIecReference = eventReference,
        ReportDisplayReference = objectReference + " [ST]",
        ExpectedOnText = "Operated",
        ExpectedOffText = "Normal",
        ImportReady = true,
        TestEnabled = true
    };

    private static IoTestPointPlan WeakProtection27Point(string id, string signalName) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F13R1",
        IpAddress = "192.168.81.14",
        SignalName = signalName,
        ObjectReference = ".Op.general",
        LogicalDevice = "AA1C1F13R1Application",
        LogicalNode = "-",
        DataObject = ".Op",
        DataAttribute = "general",
        FunctionalConstraint = "ST",
        EventLogSearchReference = ".Op",
        SourceIecReference = ".Op",
        ReportDisplayReference = "AA1C1F13R1Application/.Op.general [ST]",
        ExpectedOnText = "Operated",
        ExpectedOffText = "Normal",
        ImportReady = true,
        TestEnabled = true
    };

    private static IoTestIedPlan Ied(string name, string ipAddress, params IoTestPointPlan[] points) => new()
    {
        IedName = name,
        IpAddress = ipAddress,
        IedRole = "IED - 7SX80",
        TestPoints = points.ToList()
    };

    private static Iec61850MonitorDevice Device(
        string name,
        string ipAddress,
        params SignalDefinition[] signals)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = name,
            SclIedName = name,
            IpAddress = ipAddress,
            Port = 102
        };
        device.Signals.AddRange(signals);
        return device;
    }

    private static SignalDefinition Signal(string name, string reference) => new()
    {
        Name = name,
        ObjectReference = reference,
        FunctionalConstraint = "ST"
    };
}
