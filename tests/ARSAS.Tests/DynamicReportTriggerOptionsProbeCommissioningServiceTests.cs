namespace ARSAS.Tests;

public sealed class DynamicReportTriggerOptionsProbeCommissioningServiceTests
{
    [Fact]
    public void P0_Service_Uses_Isolated_Engine_Probe_And_Correct_0244_Target()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Services",
            "DynamicReportTriggerOptionsProbeCommissioningService.cs"));

        Assert.Contains("ProbeDynamicRcbTriggerOptionsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ProbeTriggerOptions = \"dchg gi\"", source, StringComparison.Ordinal);
        Assert.Contains("expectedCanonicalRaw=0244", source, StringComparison.Ordinal);
        Assert.Contains("forced-live proven-empty/free URCB", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P0_Service_Does_Not_Invoke_DataSet_Or_Report_Activation_APIs()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Services",
            "DynamicReportTriggerOptionsProbeCommissioningService.cs"));

        Assert.DoesNotContain(".PrepareDynamicRcbCommissioningFieldsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".DefineNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".DeleteNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".BuildDynamicPlan(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".SaveAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_UI_Has_Dedicated_CtrlShiftT_And_Explicit_TrgOpsOnly_Warning()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationUiBehavior.cs"));

        Assert.Contains("e.Key != Key.T", source, StringComparison.Ordinal);
        Assert.Contains("RunP0TriggerProbeAsync", source, StringComparison.Ordinal);
        Assert.Contains("TrgOps ONLY", source, StringComparison.Ordinal);
        Assert.Contains("canonical raw target 0244", source, StringComparison.Ordinal);
        Assert.Contains("does NOT write OptFlds, DatSet, Resv, RptEna or GI", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_Evidence_Separates_Semantic_And_Raw_Equality()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationResultWindow.xaml.cs"));

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
