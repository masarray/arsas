using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850ReportPlannerRegressionTests
{
    [Fact]
    public void BuildPlans_FastStaticPoint_RemainsEligibleForStaticReporting()
    {
        var device = BuildDevice();
        var point = BuildFastStatusPoint(device);
        point.ReportControlReference = "IED1LD0/LLN0$BR$brcb01";
        point.DataSetReference = "IED1LD0/LLN0$DataSet1";

        var plans = Iec61850ReportPlanner.BuildPlans(device, new[] { point });

        var plan = Assert.Single(plans);
        Assert.Equal("Static candidate", plan.Status);
        Assert.Contains(point, plan.Bindings);
        Assert.True(plan.Buffered);
    }

    [Fact]
    public void BuildPlans_FastPointWithoutStaticCoverage_BecomesDynamicCandidate()
    {
        var device = BuildDevice();
        var point = BuildFastStatusPoint(device);

        var plans = Iec61850ReportPlanner.BuildPlans(device, new[] { point });

        var plan = Assert.Single(plans);
        Assert.Equal("Dynamic candidate", plan.Status);
        Assert.True(plan.AllowDynamicDataSetWrites);
        Assert.Contains(point, plan.Bindings);
    }

    [Fact]
    public void BuildDynamicFallbackPlans_FastPoint_IsNotExcludedByPollingInterval()
    {
        var device = BuildDevice();
        var point = BuildFastStatusPoint(device);

        var plans = Iec61850ReportPlanner.BuildDynamicFallbackPlans(device, new[] { point });

        var plan = Assert.Single(plans);
        Assert.Equal("Dynamic candidate", plan.Status);
        Assert.Contains(point, plan.Bindings);
    }

    private static Iec61850MonitorDevice BuildDevice()
        => new()
        {
            DeviceId = "ied-1",
            Name = "IED1",
            IpAddress = "192.168.1.10",
            AllowDynamicDataSetWrites = true
        };

    private static Iec61850MonitorPoint BuildFastStatusPoint(Iec61850MonitorDevice device)
        => new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = "Breaker position",
            IecReference = "IED1LD0/XCBR1.Pos.stVal",
            FunctionalConstraint = "ST",
            IecDataType = "Dbpos",
            Category = "Position",
            PollingIntervalMs = 250
        };
}
