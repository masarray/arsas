using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P109ZFinalInspectorRegressionTests
{
    [Fact]
    public void RcbInspector_PreservesReportIdVsName_AndNeverInventsRptId()
    {
        var row = new RcbExportRow
        {
            Name = "brcb01",
            Reference = "ZIVAVRLD1/LLN0.BR.brcb01",
            Inspection = RcbInspectionEvidence.FromSource("Rpt_0", null)
        };
        Assert.Equal("brcb01", row.Name);
        Assert.Equal("Rpt_0", row.ReportIdDisplay);
        Assert.Contains("SCL", row.ReportIdSource);
        Assert.Equal("—", new RcbExportRow { Name = "brcb02" }.ReportIdDisplay);
    }

    [Fact]
    public void ReadOnlyMmsRptId_WinsOverModelWithoutChangingRcbIdentity()
    {
        var snapshot = new MmsRcbAvailabilitySnapshot
        {
            ReportId = "Rpt_0",
            CheckedAtUtc = DateTimeOffset.UtcNow,
            EnabledState = "true", Owner = "IED_owner"
        };
        var row = new RcbExportRow
        {
            Name = "brcb01",
            Inspection = RcbInspectionEvidence.FromSource("LLN0$BR$brcb01", snapshot)
        };
        Assert.Equal("Rpt_0", row.ReportIdDisplay);
        Assert.Equal("brcb01", row.Name);
        Assert.Contains("MMS", row.ReportIdSource);

        var online = row.Inspection.Properties(row, connected: true);
        var offline = row.Inspection.Properties(row, connected: false);
        Assert.Equal("true", online.Single(item => item.Property == "Enabled / RptEna").Value);
        Assert.Equal("—", offline.Single(item => item.Property == "Enabled / RptEna").Value);
        Assert.Equal("—", offline.Single(item => item.Property == "Owner").Value);
        Assert.Equal("LLN0$BR$brcb01", offline.Single(item => item.Property == "Report ID").Value);
    }

    [Fact]
    public void SourceRptIdParser_UsesExactIedApLdLnAndNameScope()
    {
        var code = Read("Services/RcbSclReportIdCatalog.cs");
        Assert.Contains("descriptor.AccessPointName", code);
        Assert.Contains("descriptor.LogicalDeviceInstance", code);
        Assert.Contains("descriptor.LogicalNodePath", code);
        Assert.Contains("exact.Length != 1", code);
        Assert.Contains("DtdProcessing.Prohibit", code);
    }

    [Fact]
    public void ActualProductionRcbWorkflow_HasIndependentInspectorAndSelection()
    {
        var window = Read("RcbMultiExportWindow.cs");
        var selection = Read("MainWindow.RcbMultiSelectPointerAuthority.cs");
        Assert.Contains("CreateReportIdColumn()", window);
        Assert.Contains("ShowInspector(row)", window);
        Assert.Contains("CloseInspector()", window);
        Assert.Contains("Inspection.Properties(row, connected)", window.Replace("row.Inspection.Properties(row, connected)", "Inspection.Properties(row, connected)"));
        Assert.Contains("FindRcbAncestor<Button>(source) != null", selection);
        Assert.DoesNotContain("MmsClientSession", window, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReport", window, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
