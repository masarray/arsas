using ArIED61850Tester;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SasOperationalUiDataSetAuthorityRegressionTests
{
    [Fact]
    public void ObjectLevelStaticDataSetMember_RemainsPresentationVisible()
    {
        var signal = new SignalDefinition
        {
            Name = "CBOpnd",
            ObjectReference = "AA1C1F13R4ADD/GGIO6.CBOpnd",
            DisplayReference = "AA1C1F13R4ADD/GGIO6.CBOpnd",
            FunctionalConstraint = "ST",
            DataSetReference = "AA1C1F13R4Application/LLN0$Digital",
            Category = "DataSet",
            Source = "ARIEC61850 signal inventory • mandatory static DataSet member"
        };

        // The normal operator policy intentionally requires an exact runtime value leaf,
        // so an object-level Siemens FCDA is not independently promoted by that policy.
        Assert.False(SasOperationalSignalPolicy.IsVisible(signal));

        // Static DataSet membership is a stronger protocol-authority contract and must
        // therefore remain visible without inventing .stVal or another runtime leaf.
        Assert.True(SasOperationalUiPolicy.IsPresentationVisible(signal));
        Assert.Equal("AA1C1F13R4ADD/GGIO6.CBOpnd", signal.DisplayReference);
        Assert.DoesNotContain(".stVal", signal.DisplayReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HiddenNonDataSetObjectLevelSignal_RemainsFilteredByNormalOperatorPolicy()
    {
        var signal = new SignalDefinition
        {
            Name = "CBOpnd",
            ObjectReference = "AA1C1F13R4ADD/GGIO6.CBOpnd",
            DisplayReference = "AA1C1F13R4ADD/GGIO6.CBOpnd",
            FunctionalConstraint = "ST",
            Category = "Status"
        };

        Assert.False(SasOperationalSignalPolicy.IsVisible(signal));
        Assert.False(SasOperationalUiPolicy.IsPresentationVisible(signal));
    }

    [Fact]
    public void UiPolicy_NeverDeletesRowsFromAuthoritativeSignalCollection()
    {
        var source = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));

        Assert.Contains("view.Filter = item =>", source, StringComparison.Ordinal);
        Assert.Contains("signal.DataSetReference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("list.RemoveAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.RemoveAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SchedulePrune", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionSubscription", source, StringComparison.Ordinal);
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
