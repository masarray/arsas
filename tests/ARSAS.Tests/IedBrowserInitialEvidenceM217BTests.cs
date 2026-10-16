using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class IedBrowserInitialEvidenceM217BTests
{
    private const string Mod = "LD/LLN0$ST$Mod$stVal";
    private const string Health = "LD/LLN0$ST$Health$stVal";
    private const string NamPlt = "LD/LLN0$DC$NamPlt$vendor";
    private const string Position = "LD/XCBR1$ST$Pos$stVal";

    private static LiveIedDataAttributeModel Attr(string mms, string fc, string type = "Enum")
        => new()
        {
            ObjectReference = IedBrowserLiveValues.CanonicalReference(mms)
                .Split('.')[0] + "." + IedBrowserLiveValues.CanonicalReference(mms)
                .Split('.')[1],
            MmsReference = mms, FunctionalConstraint = fc, SclBType = type
        };

    private static LiveIedModelDiscoveryDocument Model(bool duplicate = false)
    {
        var attributes = new List<LiveIedDataAttributeModel>
        {
            Attr(Mod, "ST"), Attr(Health, "ST"),
            Attr(NamPlt, "DC", "VisString255"), Attr(Position, "ST", "Dbpos")
        };
        if (duplicate) attributes.Add(Attr(Mod, "ST"));
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [new LiveIedDataObjectModel { Attributes = attributes }]
                }]
            }]
        };
    }

    private static TrustedSclInitialValue Value(string reference, string fc,
        object? value, string type = "Enum")
        => new() { Reference = reference, FunctionalConstraint = fc,
            Value = value, SclBType = type };

    private static Iec61850MonitorDevice Connected(LiveIedModelDiscoveryDocument model)
    {
        var device = new Iec61850MonitorDevice { LiveDiscoveryModel = model };
        device.IsConnected = true;
        return device;
    }

    [Fact]
    public void ExistingInitialMmsEvidencePopulatesLedgerAndExplorerWithoutAnotherRead()
    {
        var model = Model();
        var device = Connected(model);
        var signal = new SignalDefinition
        {
            ObjectReference = "LD/LLN0.Mod.stVal", FunctionalConstraint = "ST"
        };
        device.Signals.Add(signal);
        var incoming = new Dictionary<string, TrustedSclInitialValue>(StringComparer.Ordinal)
        {
            [Mod] = Value("LD/LLN0.Mod.stVal", "ST", 2),
            [Health] = Value("LD/LLN0.Health.stVal", "ST", 1),
            [NamPlt] = Value("LD/LLN0.NamPlt.vendor", "DC", "GE", "VisString255"),
            [Position] = Value("LD/XCBR1.Pos.stVal", "ST", "bits(40,unused=6)", "Dbpos")
        };
        var count = IedBrowserInitialEvidenceProjection.Publish(device, model,
            reference => incoming.TryGetValue(reference, out var result) ? result : null);
        Assert.Equal(4, count);
        Assert.True(device.LiveValues.TryGet("LD/LLN0.Mod.stVal", "ST", out var mod));
        Assert.Equal("2", mod.Value);
        Assert.Equal("MMS Read", mod.Source);
        Assert.Equal(device.LiveValues.Association, mod.Association);
        Assert.Equal("2", signal.Value);
        Assert.True(device.LiveValues.TryGet("LD/LLN0.NamPlt.vendor", "DC", out var name));
        Assert.Equal("GE", name.Value);
        Assert.True(device.LiveValues.TryGet("LD/XCBR1.Pos.stVal", "ST", out var pos));
        Assert.Equal("bits(40,unused=6)", pos.Value); // DPC decoding remains presentation-only
        Assert.Equal(0, IedBrowserInitialEvidenceProjection.Publish(device, Model(),
            _ => throw new Exception("replaced model must not be queried")));
    }

    [Fact]
    public void FcTypeCaseAndDuplicateDeclarationsFailClosed()
    {
        var model = Model(duplicate: true);
        var device = Connected(model);
        var reads = new Dictionary<string, TrustedSclInitialValue>(StringComparer.Ordinal)
        {
            [Mod] = Value("LD/LLN0.Mod.stVal", "ST", 1),
            [Health] = Value("LD/LLN0.Health.stVal", "MX", 2),
            [NamPlt] = Value("LD/LLN0.NamPlt.vendor", "DC", "GE", "Enum"),
            [Position] = Value("LD/XCBR1.pos.stVal", "ST", 2, "Dbpos")
        };
        Assert.Equal(0, IedBrowserInitialEvidenceProjection.Publish(device, model,
            reference => reads.TryGetValue(reference, out var result) ? result : null));
        Assert.False(device.LiveValues.TryGet("LD/LLN0.Mod.stVal", "ST", out _));
        Assert.False(device.LiveValues.TryGet("LD/LLN0.Health.stVal", "ST", out _));
    }

    [Fact]
    public void ReportOutranksLateInitialReadAndReconnectFencesOldCallbacks()
    {
        var model = Model();
        var device = Connected(model);
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            "LD/LLN0.Mod.stVal", "ST", "3", "Good", "report-t", "Report"));
        Assert.Equal(1, IedBrowserInitialEvidenceProjection.Publish(device, model,
            reference => reference == Mod ? Value("LD/LLN0.Mod.stVal", "ST", 1) : null));
        Assert.True(device.LiveValues.TryGet("LD/LLN0.Mod.stVal", "ST",
            out var observed, preferReport: true));
        Assert.Equal("3", observed.Value);
        device.IsConnected = false;
        device.IsConnected = true;
        Assert.False(device.LiveValues.PublishForAssociation(association,
            Mod, "ST", "999", "", "", "MMS Read"));
        Assert.False(device.LiveValues.TryGet(Mod, "ST", out _));
    }

    [Fact]
    public void UnverifiedUnavailableAndNonScalarInputsNeverBecomeFakeMeasurements()
    {
        var model = Model();
        var device = Connected(model);
        var entries = new Dictionary<string, TrustedSclInitialValue>
        {
            [Mod] = Value("LD/LLN0.Mod.stVal", "ST", "Not read"),
            [Health] = Value("LD/LLN0.Health.stVal", "ST", new object()),
            [NamPlt] = Value("LD/LLN0.NamPlt.vendor", "DC", "Structure(a=1)", "VisString255"),
            [Position] = Value("LD/XCBR1.Pos.stVal", "ST", DateTime.UtcNow, "Dbpos")
        };
        Assert.Equal(0, IedBrowserInitialEvidenceProjection.Publish(device, model,
            reference => entries.TryGetValue(reference, out var result) ? result : null));
        Assert.Equal(0, device.LiveValues.Version - 1); // association established; no values published
    }
}
