namespace ARSAS.Tests;

public sealed class DynamicReportOptionalFieldsProbeCommissioningServiceTests
{
    [Fact]
    public void P1_Service_Uses_Isolated_Engine_Probe_And_Correct_061800_Target()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Services",
            "DynamicReportOptionalFieldsProbeCommissioningService.cs"));

        Assert.Contains("ProbeDynamicRcbOptionalFieldsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ProbeOptionalFields = \"reason-for-inclusion data-set-name\"", source, StringComparison.Ordinal);
        Assert.Contains("expectedCanonicalRaw=061800", source, StringComparison.Ordinal);
        Assert.Contains("forced-live proven-empty/free URCB", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P1_Service_Does_Not_Invoke_TrgOps_DataSet_Or_Report_Activation_APIs()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Services",
            "DynamicReportOptionalFieldsProbeCommissioningService.cs"));

        Assert.DoesNotContain("ProbeDynamicRcbTriggerOptionsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".PrepareDynamicRcbCommissioningFieldsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".DefineNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".DeleteNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".BuildDynamicPlan(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".SaveAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_UI_Has_Dedicated_CtrlShiftO_And_Explicit_OptFldsOnly_Warning()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationUiBehavior.cs"));

        Assert.Contains("e.Key != Key.O", source, StringComparison.Ordinal);
        Assert.Contains("RunP1OptionalFieldsProbeAsync", source, StringComparison.Ordinal);
        Assert.Contains("OptFlds ONLY", source, StringComparison.Ordinal);
        Assert.Contains("canonical raw target 061800", source, StringComparison.Ordinal);
        Assert.Contains("does NOT write TrgOps, DatSet, Resv, RptEna or GI", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_Evidence_Separates_Semantic_And_Raw_Equality()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationResultWindow.P1.cs"));

        Assert.Contains("Requested semantic match", source, StringComparison.Ordinal);
        Assert.Contains("Requested raw exact", source, StringComparison.Ordinal);
        Assert.Contains("Requested padding-only diff", source, StringComparison.Ordinal);
        Assert.Contains("Restore semantic match", source, StringComparison.Ordinal);
        Assert.Contains("Restore raw exact", source, StringComparison.Ordinal);
        Assert.Contains("Restore padding-only diff", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
