namespace ARSAS.Tests;

public sealed class FatScrollStabilityRegressionTests
{
    [Fact]
    public void Recovery_KeepsBuild1868Value1Value2GridContract()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("Header = \"TEST\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"SIGNAL\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"IEC REFERENCE\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"TYPE\"", ux, StringComparison.Ordinal);
        Assert.Contains("Header = \"LIVE VALUE\"", ux, StringComparison.Ordinal);
        Assert.Contains("Header = slot == FatValueSlot.Value1 ? \"VALUE 1\" : \"VALUE 2\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"STATUS\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"RESULT\"", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("ON · RELAY TIME", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("OFF · RELAY TIME", ux, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_UsesNonRecyclingVirtualizationWithoutAddingLiveSubscriptions()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatScrollStability.cs"));

        Assert.Contains("grid.EnableRowVirtualization = true;", source, StringComparison.Ordinal);
        Assert.Contains("grid.EnableColumnVirtualization = false;", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode.Standard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PropertyChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.CurrentValue =", source, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
