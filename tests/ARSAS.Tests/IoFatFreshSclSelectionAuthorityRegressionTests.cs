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
    public void SclBridge_UsesExistingEngineeringSelectionWhenItWasAlreadyDecided()
    {
        var source = Read("Services/IoTesting/IoFatEngineeringSelectionBridge.cs");

        Assert.Contains("if (preserveExistingEngineeringSelection)", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled != signal.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = signal.IsSelected;", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = true;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = preserveExistingEngineeringSelection;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshSclBridge_DoesNotMergeSelectionWithRemovedSignalsDisposition()
    {
        var source = Read("Services/IoTesting/IoFatEngineeringSelectionBridge.cs");

        Assert.Contains("FatDisposition remains orthogonal", source, StringComparison.Ordinal);
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
