using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class WorkspaceNoiseP1RegressionTests
{
    [Theory]
    [InlineData("Good", Iec61850QualityPresentation.Good)]
    [InlineData("Good [0x0000]", Iec61850QualityPresentation.Good)]
    [InlineData("Questionable • oldData", Iec61850QualityPresentation.Attention)]
    [InlineData("Good • substituted", Iec61850QualityPresentation.Attention)]
    [InlineData("Invalid", Iec61850QualityPresentation.Bad)]
    [InlineData("Failure", Iec61850QualityPresentation.Bad)]
    [InlineData("Unknown", Iec61850QualityPresentation.Unknown)]
    public void QualityPresentation_SeparatesHealthyAttentionAndBadWithoutChangingProcessState(string quality, string expected)
        => Assert.Equal(expected, Iec61850QualityPresentation.Classify(quality));

    [Fact]
    public void EventLog_ContainsQualityAttentionInsideDedicatedBadge()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var models = File.ReadAllText(FindRepoFile(Path.Combine("Models", "MonitorModels.cs")));
        var section = Slice(xaml, "<!-- EVENT LOG -->", "<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->");

        Assert.Contains("x:Key=\"EventQualityBadgeTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CellTemplate=\"{StaticResource EventQualityBadgeTemplate}\"", section, StringComparison.Ordinal);
        Assert.Contains("process state and signal quality are shown separately", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QualityTone => Iec61850QualityPresentation.Classify(Quality)", models, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Quality\" Binding=\"{Binding Quality}\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_UsesContainedLevelBadgeAndRail_NotWholeCellSeverityFlood()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var section = Slice(xaml, "<!-- DIAGNOSTICS -->", "</TabControl>");

        Assert.Contains("x:Key=\"DiagnosticLevelBadgeTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CellTemplate=\"{StaticResource DiagnosticLevelBadgeTemplate}\"", section, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"3,0,0,0\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<DataGrid.CellStyle>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Background\" Value=\"#FFF1F2\"/>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Background\" Value=\"#FFFAEB\"/>", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Alarm_RemovesCrypticMicrocopyAndKeepsOperationalStateSemantics()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var section = Slice(xaml, "<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->", "<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->");

        Assert.Contains("Unacknowledged flashes • acknowledged steady • returned awaits ACK", section, StringComparison.Ordinal);
        Assert.Contains("Text=\"IEDs\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("FLASH = UNACK", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Select fascia", section, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"8.2\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"8.8\"", section, StringComparison.Ordinal);
        Assert.Contains("Value=\"ActiveUnacknowledged\"", section, StringComparison.Ordinal);
        Assert.Contains("Value=\"ActiveAcknowledged\"", section, StringComparison.Ordinal);
        Assert.Contains("Value=\"ReturnedUnacknowledged\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Goose_IdleStateUsesProgressiveDisclosureWithoutRemovingEngineeringControls()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var code = File.ReadAllText(FindRepoFile("MainWindow.GooseSubscriber.cs"));
        var section = Slice(xaml, "<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->", "<!-- DIAGNOSTICS -->");

        Assert.Contains("x:Name=\"GooseCaptureOptionsExpander\"", section, StringComparison.Ordinal);
        Assert.Contains("Header=\"Capture options\" IsExpanded=\"False\"", section, StringComparison.Ordinal);
        Assert.Contains("GooseCaptureFilter", section, StringComparison.Ordinal);
        Assert.Contains("RefreshGooseModels_Click", section, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding GooseDataSetInspectorVisibility}\"", section, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding GooseStartVisibility}\"", section, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding GooseStopVisibility}\"", section, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding GooseClearVisibility}\"", section, StringComparison.Ordinal);
        Assert.Contains("GooseDataSetInspectorVisibility => SelectedGooseStream is null", code, StringComparison.Ordinal);
        Assert.Contains("GooseClearVisibility => GooseStreams.Count > 0", code, StringComparison.Ordinal);
        Assert.Contains("Raise(nameof(GooseDataSetInspectorVisibility))", code, StringComparison.Ordinal);
        Assert.Contains("Raise(nameof(GooseClearVisibility))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_KeepsReviewedAriecEnginePinUntouched()
    {
        var root = Path.GetDirectoryName(FindRepoFile("MainWindow.xaml"))!;
        var engineLock = File.ReadAllText(Path.Combine(root, "engines", "ARIEC61850.lock.json"));

        Assert.Contains("becda399b4a3ae34831215fc915798b4f846c1be", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 81", engineLock, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var a = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(a >= 0, $"Start marker not found: {start}");
        var b = source.IndexOf(end, a + start.Length, StringComparison.Ordinal);
        Assert.True(b > a, $"End marker not found: {end}");
        return source[a..b];
    }

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

        throw new FileNotFoundException(relativePath);
    }
}
