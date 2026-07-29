using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatReportPreviewServiceTests
{
    [Fact]
    public void BuildHtml_IsScopedToSelectedIed_AndMarksLiveSessionDraft()
    {
        var selected = BuildIed("IED_A", "192.168.1.10", "TP-A");
        var other = BuildIed("IED_B", "192.168.1.11", "TP-B");
        var project = BuildProject(selected, other);

        var html = IoFatReportPreviewService.BuildHtml(
            project,
            selected,
            draft: true,
            new DateTimeOffset(2026, 7, 29, 7, 0, 0, TimeSpan.Zero));

        Assert.Contains("IED_A evidence report", html, StringComparison.Ordinal);
        Assert.Contains("TP-A", html, StringComparison.Ordinal);
        Assert.Contains("DRAFT / LIVE", html, StringComparison.Ordinal);
        Assert.DoesNotContain("IED_B", html, StringComparison.Ordinal);
        Assert.DoesNotContain("TP-B", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHtml_EncodesImportedText()
    {
        var ied = BuildIed("IED<&>", "192.168.1.20", "Signal <unsafe>");
        var html = IoFatReportPreviewService.BuildHtml(BuildProject(ied), ied, draft: false);

        Assert.Contains("IED&lt;&amp;&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Signal &lt;unsafe&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal <unsafe>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIedScopedProject_PreservesIdentityAndOnlySelectedIed()
    {
        var first = BuildIed("IED_A", "192.168.1.10", "TP-A");
        var second = BuildIed("IED_B", "192.168.1.11", "TP-B");
        var project = BuildProject(first, second);

        var scoped = IoFatReportPreviewService.CreateIedScopedProject(project, second);

        Assert.Equal(project.ProjectId, scoped.ProjectId);
        Assert.Equal(project.SourceWorkbookSha256, scoped.SourceWorkbookSha256);
        Assert.Single(scoped.Ieds);
        Assert.Same(second, scoped.Ieds[0]);
    }

    [Fact]
    public void PreviewWorkspace_PortsAriecBrowserPattern_AndKeepsProgressOnIedCard()
    {
        var previewSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var xaml = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));

        Assert.Contains("WebBrowser", previewSource, StringComparison.Ordinal);
        Assert.Contains("NavigateToString", previewSource, StringComparison.Ordinal);
        Assert.Contains("Print Preview", previewSource, StringComparison.Ordinal);
        Assert.Contains("CreateIedScopedProject", previewSource, StringComparison.Ordinal);
        Assert.Contains("RemoveMainPreparationSurface", previewSource, StringComparison.Ordinal);
        Assert.Contains("workspaceGrid.Children.Remove(preparationSurface)", previewSource, StringComparison.Ordinal);
        Assert.Contains("ClearStalePreparationFlags", previewSource, StringComparison.Ordinal);
        Assert.Contains("CardProgress", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IoFatPreparation_UsesCachedReconnect_AndRetriesReportPlanBeforePollingFallback()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("Fast reconnect", source, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("SettleIoFatReportPriorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("rebuilding the report plan once", source, StringComparison.Ordinal);
        Assert.Contains("configured RCB → temporary dynamic DataSet/URCB", source, StringComparison.Ordinal);
        Assert.Contains("IsReportSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
    }

    private static IoTestProject BuildProject(params IoTestIedPlan[] ieds) => new()
    {
        ProjectId = "PROJECT-1",
        SchemaVersion = "ARSAS-FAT-IO-1.0",
        ProjectName = "Preview Project",
        SourceWorkbookName = "source.xlsx",
        SourceWorkbookSha256 = "abcdef0123456789abcdef0123456789",
        Ieds = ieds.ToList()
    };

    private static IoTestIedPlan BuildIed(string name, string ip, string signalName) => new()
    {
        IedName = name,
        IpAddress = ip,
        TestPoints = new List<IoTestPointPlan>
        {
            new()
            {
                TestPointId = $"{name}-TP-1",
                IedName = name,
                IpAddress = ip,
                SignalName = signalName,
                ObjectReference = $"{name}LD/GGIO1.Ind1.stVal",
                FunctionalConstraint = "ST",
                ExpectedOnText = "true",
                ExpectedOffText = "false"
            }
        }
    };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
