namespace ARSAS.Tests;

public sealed class NativeFatP4CCanonicalColumnContractTests
{
    [Fact]
    public void P4C_FatExposesExactNineColumnExplorerEvidenceContract()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var tabSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        var signal = source.IndexOf("AddCanonicalTextColumn(\"Signal\"", StringComparison.Ordinal);
        var telegram = source.IndexOf("AddCanonicalTextColumn(\"IEC Telegram\"", StringComparison.Ordinal);
        var quality = source.IndexOf("AddCanonicalTextColumn(\"Quality\"", StringComparison.Ordinal);
        var liveValue = source.IndexOf("AddCanonicalTemplateColumn(\"Live Value\"", StringComparison.Ordinal);
        var value1 = source.IndexOf("\"Value 1\", NativeFatEvidenceField.Value1", StringComparison.Ordinal);
        var timestamp1 = source.IndexOf("\"V1 Timestamp\", NativeFatEvidenceField.Value1Timestamp", StringComparison.Ordinal);
        var value2 = source.IndexOf("\"Value 2\", NativeFatEvidenceField.Value2", StringComparison.Ordinal);
        var timestamp2 = source.IndexOf("\"V2 Timestamp\", NativeFatEvidenceField.Value2Timestamp", StringComparison.Ordinal);
        var result = source.IndexOf("\"Result\", NativeFatEvidenceField.Result", StringComparison.Ordinal);

        Assert.True(signal >= 0);
        Assert.True(telegram > signal);
        Assert.True(quality > telegram);
        Assert.True(liveValue > quality);
        Assert.True(value1 > liveValue);
        Assert.True(timestamp1 > value1);
        Assert.True(value2 > timestamp1);
        Assert.True(timestamp2 > value2);
        Assert.True(result > timestamp2);

        Assert.Contains("_nativeFatCanonicalGrid.Columns.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("ApplyNativeFatP4CColumnContract();", gridSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyNativeFatP4CColumnContract();", tabSource, StringComparison.Ordinal);
        Assert.Contains("_nativeFatCanonicalGrid.ItemsSource = device?.Points;", gridSource, StringComparison.Ordinal);

        Assert.DoesNotContain("\"Status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Address\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Message\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Data Reference\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableCollection<Iec61850MonitorPoint>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Iec61850MonitorPoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P4C_TimestampColumnsShareTheEvidenceRefreshAuthority()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var overlay = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatCanonicalEvidenceOverlay.cs"));

        Assert.Contains("NativeFatEvidenceField.Value1Timestamp", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value2Timestamp", source, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly = true", source, StringComparison.Ordinal);
        Assert.Contains("Columns.OfType<NativeFatEvidenceColumn>()", gridSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value1Timestamp => TimestampValue(slot.Value1Evidence)", overlay, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value2Timestamp => TimestampValue(slot.Value2Evidence)", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void P4C_EvidenceEventSurfacesOneOfTwoThenTwoOfTwoWithCompleteResult()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));
        var overlay = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatCanonicalEvidenceOverlay.cs"));

        Assert.Contains("_nativeFatArmCoordinator.EvidenceChanged += NativeFatObservationStatus_EvidenceChanged", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", source, StringComparison.Ordinal);
        Assert.Contains("{observations} / 2 observations", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value1", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value2", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Result", source, StringComparison.Ordinal);
        Assert.Contains("HasValue1(slot) && HasValue2(slot) ? \"COMPLETE\"", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void P4C_CanonicalGridBuilderHasNoLegacyColumnInstallationPath()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));

        Assert.Contains("ApplyNativeFatP4CColumnContract();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Type\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Address\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Message\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Data Reference\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTextColumn(\"Timestamp\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCanonicalTemplateColumn(\"Value\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new NativeFatEvidenceColumn(this, \"Value 1\"", source, StringComparison.Ordinal);
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
