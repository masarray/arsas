using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatApplicationWrapperBindingRegressionTests
{
    private readonly IoTestLiveBindingService _binding = new();

    [Fact]
    public void TangguhApplicationAddWrapper_MatchesDiscoveryThatOmitsAdd_WhenUnique()
    {
        var project = Project("AA1C1F13R4Application/ADD/GGIO1.LocOpnCMDsta.stVal");
        var device = Device();
        device.Signals.Add(Signal("AA1C1F13R4Application/GGIO1.LocOpnCMDsta.stVal", "ST"));

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.SignalBoundCount);
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void TangguhApplicationAddWrapper_MatchesCanonicalAddDomain_WhenUnique()
    {
        var project = Project("AA1C1F13R4Application/ADD/GGIO1.LocOpnCMDsta.stVal");
        var device = Device();
        device.Signals.Add(Signal("ADD/GGIO1.LocOpnCMDsta.stVal", "ST"));

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.SignalBoundCount);
        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void SameLnDoDaTailInTwoLogicalDevices_RemainsBlockedAsAmbiguous()
    {
        var project = Project("AA1C1F13R4Application/ADD/GGIO1.LocOpnCMDsta.stVal");
        var device = Device();
        device.Signals.Add(Signal("ADD/GGIO1.LocOpnCMDsta.stVal", "ST"));
        device.Signals.Add(Signal("CTRL/GGIO1.LocOpnCMDsta.stVal", "ST"));

        var summary = _binding.Bind(project, new[] { device });
        var point = project.Ieds[0].TestPoints[0];

        Assert.Equal(0, summary.SignalBoundCount);
        Assert.Equal(1, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.SignalNotFound, point.LiveBindingState);
        Assert.Contains("more than one", point.LiveBindingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunctionalConstraintMismatch_IsNeverAcceptedByWrapperFallback()
    {
        var project = Project("AA1C1F13R4Application/ADD/GGIO1.LocOpnCMDsta.stVal");
        var device = Device();
        device.Signals.Add(Signal("AA1C1F13R4Application/GGIO1.LocOpnCMDsta.stVal", "MX"));

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(0, summary.SignalBoundCount);
        Assert.Equal(1, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.SignalNotFound, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    private static IoTestProject Project(string reference)
    {
        var project = new IoTestProject
        {
            ProjectId = "TANGGUH-UCC",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "Tangguh UCC Project - Onshore EPCI",
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = "AA1C1F13R4",
                    IpAddress = "192.168.81.17",
                    IedRole = "BCU",
                    TestPoints =
                    {
                        new IoTestPointPlan
                        {
                            TestPointId = "UCC-IEC-0698",
                            IedName = "AA1C1F13R4",
                            IpAddress = "192.168.81.17",
                            SignalName = "Selector local (LCC/CRP) control: CB Open command",
                            ObjectReference = reference,
                            SourceIecReference = "ADD/GGIO1.LocOpnCMDsta",
                            EventLogSearchReference = "ADD/GGIO1.LocOpnCMDsta",
                            ReportDisplayReference = reference + " [ST]",
                            LogicalDevice = "AA1C1F13R4Application",
                            LogicalNode = "GGIO1",
                            DataAttribute = "stVal",
                            FunctionalConstraint = "ST",
                            ExpectedOnText = "Active",
                            ExpectedOffText = "InActive",
                            ImportReady = true,
                            BindingStatus = "CID_DATASET_EXACT"
                        }
                    }
                }
            }
        };
        project.InitializeRuntimeNotifications();
        return project;
    }

    private static SignalDefinition Signal(string reference, string functionalConstraint) => new()
    {
        Name = "LocOpnCMDsta",
        ObjectReference = reference,
        FunctionalConstraint = functionalConstraint,
        Category = "Status",
        DataType = "Boolean"
    };

    private static Iec61850MonitorDevice Device() => new()
    {
        Name = "AA1C1F13R4",
        SclIedName = "AA1C1F13R4",
        IpAddress = "192.168.81.17",
        Port = 102,
        Status = "Ready"
    };
}
