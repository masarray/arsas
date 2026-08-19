using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DynamicReportQualificationIdentityTests
{
    [Fact]
    public void Fingerprint_IsOrderInvariantAndSelectionInvariant()
    {
        var device = Device();
        var first = Signals();
        var second = Signals().Reverse().ToArray();
        second[0].IsSelected = !second[0].IsSelected;

        var a = DynamicReportQualificationIdentity.Build(device, first);
        var b = DynamicReportQualificationIdentity.Build(device, second);

        Assert.Equal(a.StableIdentityKey, b.StableIdentityKey);
        Assert.Equal(a.ModelFingerprint, b.ModelFingerprint);
    }

    [Fact]
    public void SclRevision_ChangesFingerprintAndProfileRevision()
    {
        var deviceA = Device();
        var deviceB = Device();
        deviceB.SclSourceSha256 = "bbbbbbbb";

        var a = DynamicReportQualificationIdentity.Build(deviceA, Signals());
        var b = DynamicReportQualificationIdentity.Build(deviceB, Signals());

        Assert.NotEqual(a.ModelFingerprint, b.ModelFingerprint);
        Assert.Equal("aaaaaaaa", a.ProfileRevision);
        Assert.Equal("bbbbbbbb", b.ProfileRevision);
    }

    [Fact]
    public void ModelSignalShapeChange_InvalidatesFingerprint()
    {
        var a = DynamicReportQualificationIdentity.Build(Device(), Signals(firstDataType: "BOOLEAN"));
        var b = DynamicReportQualificationIdentity.Build(Device(), Signals(firstDataType: "FLOAT32"));

        Assert.NotEqual(a.ModelFingerprint, b.ModelFingerprint);
    }

    [Fact]
    public void EmptyModel_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DynamicReportQualificationIdentity.Build(Device(), Array.Empty<SignalDefinition>()));
    }

    [Fact]
    public void EndpointFallback_IsUsedOnlyWithoutNamedIedIdentity()
    {
        var device = Device();
        device.SclIedName = string.Empty;
        device.Name = string.Empty;
        device.DeviceId = "dev-1";
        device.IpAddress = "192.168.81.17";
        device.Port = 102;

        var identity = DynamicReportQualificationIdentity.Build(device, Signals());

        Assert.Equal("endpoint:192.168.81.17:102", identity.StableIdentityKey);
        Assert.Equal("dev-1", identity.Model);
    }

    private static Iec61850MonitorDevice Device()
        => new()
        {
            DeviceId = "device-1",
            Name = "Q0",
            IpAddress = "192.168.81.17",
            Port = 102,
            SclIedName = "SIPROTEC-Q0",
            SclSourceSha256 = "aaaaaaaa"
        };

    private static SignalDefinition[] Signals(string firstDataType = "BOOLEAN")
        =>
        [
            new SignalDefinition
            {
                IsSelected = true,
                ObjectReference = "LD0/GGIO1.Ind1.stVal",
                FunctionalConstraint = "ST",
                DataType = firstDataType,
                LogicalNode = "GGIO1",
                DataObject = "Ind1"
            },
            new SignalDefinition
            {
                IsSelected = false,
                ObjectReference = "LD0/MMXU1.Hz.instMag.f",
                FunctionalConstraint = "MX",
                DataType = "FLOAT32",
                LogicalNode = "MMXU1",
                DataObject = "Hz"
            }
        ];
}
