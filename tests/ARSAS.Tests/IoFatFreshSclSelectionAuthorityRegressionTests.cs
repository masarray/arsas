namespace ARSAS.Tests;

public sealed class IoFatFreshSclSelectionAuthorityRegressionTests
{
    [Fact]
    public void DirectSclImporter_DefaultsStaticDatasetRowsToChecked()
    {
        var source = Read("Services/IoTesting/IoFatSclProjectImportService.cs");

        Assert.Contains("TestEnabled = true,", source, StringComparison.Ordinal);
        Assert.Contains("ImportReady = true,", source, StringComparison.Ordinal);
        Assert.Contains("BindingStatus = \"SCL_DATASET_AUTHORITY\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshSclBridge_FatSelectionWinsOverOlderEngineeringSelection()
    {
        var source = Read("Services/IoTesting/IoFatEngineeringSelectionBridge.cs");

        Assert.Contains("raw SCL import is a fresh FAT selection authority", source, StringComparison.Ordinal);
        Assert.Contains("if (point.IsIncludedInFat && !point.TestEnabled)", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = true;", source, StringComparison.Ordinal);
        Assert.Contains("var selected = point.TestEnabled && point.IsIncludedInFat;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var enabled = signal.IsSelected && point.IsIncludedInFat;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshSclBridge_DoesNotMergeSelectionWithRemovedSignalsDisposition()
    {
        var source = Read("Services/IoTesting/IoFatEngineeringSelectionBridge.cs");

        Assert.Contains("Keep Removed Signals disposition independent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.RestoreToFat();\n                point.TestEnabled = true;", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
