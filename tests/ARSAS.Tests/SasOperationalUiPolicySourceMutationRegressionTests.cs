namespace ARSAS.Tests;

public sealed class SasOperationalUiPolicySourceMutationRegressionTests
{
    [Fact]
    public void DataGridPolicy_IsPresentationOnly_NotAuthoritativeCollectionMutation()
    {
        var source = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));

        Assert.Contains("view.Filter = item =>", source, StringComparison.Ordinal);
        Assert.Contains("SasOperationalSignalPolicy.IsVisible(signal)", source, StringComparison.Ordinal);
        Assert.Contains("signal.DataSetReference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveAt(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SchedulePrune", source, StringComparison.Ordinal);
        Assert.DoesNotContain("void Prune", source, StringComparison.Ordinal);
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
