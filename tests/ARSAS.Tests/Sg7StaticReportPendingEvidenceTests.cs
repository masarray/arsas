using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// An empty process image is not a WPF rendering defect: show exact
/// DataSet/RCB provenance while never claiming a Report was received.
/// </summary>
public sealed class Sg7StaticReportPendingEvidenceTests
{
    private static SignalDefinition Meter(string dataset, string rcb) => new()
    {
        ObjectReference = "Meter/ACsrcMMXU1.A.phsA.cVal.mag.f",
        FunctionalConstraint = "MX",
        DataSetReference = dataset,
        ReportControlReference = rcb
    };

    [Fact]
    public void MissingMeterImageNamesExpectedStaticRcbWithoutFabricatingRptEna()
    {
        var reason = Iec61850MonitorRuntime.StaticReportAwaitingReason(
            Meter("TT6DataSet4", "SG7_BCUMaster/LLN0.BR.BrcbI01"));
        Assert.Contains("TT6DataSet4", reason);
        Assert.Contains("BrcbI01", reason);
        Assert.Contains("awaiting first routed InformationReport", reason);
        Assert.Contains("RptEna/GI not yet proven", reason);
        Assert.Contains("polling disabled", reason);
    }

    [Fact]
    public void MissingRcbMappingCannotBePresentedAsArmed()
    {
        var reason = Iec61850MonitorRuntime.StaticReportAwaitingReason(
            Meter("TT3DataSet02", ""));
        Assert.Contains("no matched RCB", reason);
        Assert.DoesNotContain("receiving", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingDataSetCannotInheritAdjacentIedRcb()
    {
        var reason = Iec61850MonitorRuntime.StaticReportAwaitingReason(
            Meter("", "SG7_BCUMaster/LLN0.BR.BrcbA01"));
        Assert.Contains("No verified static DataSet member", reason);
        Assert.DoesNotContain("BrcbA01", reason);
    }

    [Fact]
    public void RuntimeCreatesPendingStatusWithoutStartingHiddenMmsPolling()
    {
        var source = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("StaticReportAwaitingReason(signal)", source);
        Assert.Contains("Awaiting first static InformationReport", source);
        Assert.Contains("NextPollUtc = staticDataSetReportOnly ? DateTime.MaxValue", source);
        Assert.DoesNotContain("RptEna succeeded", source);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
