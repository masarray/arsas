namespace ARSAS.Tests;

public sealed class FatControlSafetyDefaultsRegressionTests
{
    [Fact]
    public void CommandSafetyDefaults_AreAppliedToModelOnceAndRemainOperatorEditable()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.CommandPanelUx.cs"));
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("ConditionalWeakTable<SignalDefinition, Marker> _controlSafetyDefaultsApplied", source, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultControlSafetyChecks(signal);", source, StringComparison.Ordinal);
        Assert.Contains("current.ControlInterlockCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("current.ControlSynchroCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("the periodic command-panel UX refresh must never force a user choice back on", source, StringComparison.Ordinal);

        Assert.Contains("Content=\"Interlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding ControlInterlockCheck, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Sync\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding ControlSynchroCheck, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("IsChecked=\"True\"", ExtractChecksColumn(xaml), StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractChecksColumn(string source)
    {
        var start = source.IndexOf("<DataGridTemplateColumn Header=\"Checks\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("</DataGridTemplateColumn>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..(end + "</DataGridTemplateColumn>".Length)];
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
