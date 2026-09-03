using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatP52BindingRegressionTests
{
    [Fact]
    public void SclAuthority_StaticDisplayReferenceBindsWhenRuntimeLeafDiffers()
    {
        const string staticReference = "AA1E1F06R4VI3p1_Energy/XPRE_MMTR1.DmdWh";
        const string runtimeReference = "AA1E1F06R4VI3p1_Energy/XPRE_MMTR1.DmdWh.mag.f";

        var point = new IoTestPointPlan
        {
            TestPointId = "scl-energy-dmdwh",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "DmdWh",
            ObjectReference = staticReference,
            SourceIecReference = staticReference,
            ReportDisplayReference = staticReference,
            EventLogSearchReference = staticReference,
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataSetName = "AA1E1F06R4Application/LLN0.Analog",
            SignalAddress = "scl-source",
            SourceRow = 22,
            ImportReady = true,
            TestEnabled = true,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus
        };
        var ied = new IoTestIedPlan
        {
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            TestPoints = [point]
        };
        var signal = new SignalDefinition
        {
            Name = "DmdWh",
            ObjectReference = runtimeReference,
            DisplayReference = staticReference,
            FunctionalConstraint = "MX",
            DataSetReference = "AA1E1F06R4Application/LLN0.Analog",
            Source = "ARIEC61850 signal inventory • mandatory static DataSet member • primary leaf unresolved"
        };
        var device = new Iec61850MonitorDevice
        {
            Name = "AA1E1F06R4",
            SclIedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102
        };
        device.Signals.Add(signal);

        var result = new IoTestSignalSelectionService().Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        var match = Assert.Single(result.Matches);
        Assert.Same(signal, match.Signal);
        Assert.Equal(point, match.TestPoint);
    }

    [Fact]
    public void LegacyWorkbook_DoesNotUseDisplayReferenceAsHiddenAlternateIdentity()
    {
        const string importedReference = "IED1ADD/GGIO1.Expected.stVal";
        var point = new IoTestPointPlan
        {
            TestPointId = "legacy-001",
            IedName = "IED1",
            IpAddress = "192.168.81.10",
            SignalName = "Expected",
            ObjectReference = importedReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "Inactive",
            ImportReady = true,
            TestEnabled = true,
            BindingStatus = "CID_DATASET_EXACT"
        };
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.168.81.10",
            TestPoints = [point]
        };
        var device = new Iec61850MonitorDevice
        {
            Name = "IED1",
            SclIedName = "IED1",
            IpAddress = "192.168.81.10",
            Port = 102
        };
        device.Signals.Add(new SignalDefinition
        {
            Name = "Different live signal",
            ObjectReference = "IED1ADD/GGIO1.Different.stVal",
            DisplayReference = importedReference,
            FunctionalConstraint = "ST"
        });

        var result = new IoTestSignalSelectionService().Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Single(result.MissingPoints);
    }
}
