namespace ARSAS.Tests;

public sealed class NativeFatP4CCanonicalColumnContractTests
{
    [Fact]
    public void P4C_FatExposesExactlyExplorerColumnsPlusEvidence()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));
        var tabSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));

        var signal = source.IndexOf("AddCanonicalTextColumn(\"Signal\"", StringComparison.Ordinal);
        var telegram = source.IndexOf("AddCanonicalTextColumn(\"IEC Telegram\"", StringComparison.Ordinal);
        var quality = source.IndexOf("AddCanonicalTextColumn(\"Quality\"", StringComparison.Ordinal);
        var liveValue = source.IndexOf("AddCanonicalTemplateColumn(\"Live Value\"", StringComparison.Ordinal);
        var value1 = source.IndexOf("\"Value 1\", NativeFatEvidenceField.Value1", StringComparison.Ordinal);
        var value2 = source.IndexOf("\"Value 2\", NativeFatEvidenceField.Value2", StringComparison.Ordinal);
        var result = source.IndexOf("\"Result\", NativeFatEvidenceField.Result", StringComparison.Ordinal);

        Assert.True(signal >= 0);
        Assert.True(telegram > signal);
        Assert.True(quality > telegram);
        Assert.True(liveValue > quality);
        Assert.True(value1 > liveValue);
        Assert.True(value2 > value1);
        Assert.True(result > value2);

        Assert.Contains("_nativeFatCanonicalGrid.Columns.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("ApplyNativeFatP4CColumnContract();", tabSource, StringComparison.Ordinal);
        Assert.Contains("_nativeFatCanonicalGrid.ItemsSource = device?.Points;", gridSource, StringComparison.Ordinal);

        Assert.DoesNotContain("\"Status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Address\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Message\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Data Reference\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Timestamp\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableCollection<Iec61850MonitorPoint>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Iec61850MonitorPoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P4C_ColumnBindingsUseCanonicalExplorerRowProperties()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));

        Assert.Contains("nameof(Iec61850MonitorPoint.SignalName)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorPoint.IecTelegram)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorPoint.Quality)", source, StringComparison.Ordinal);
        Assert.Contains("\"ProcessValueBadgeTemplate\"", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceColumn", source, StringComparison.Ordinal);
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
