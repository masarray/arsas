namespace ARSAS.Tests;

public sealed class WpfStaIsolationContractTests
{
    [Fact]
    public void AllHistoricallyTimingOutWpfSuitesUseOneExclusiveCollection()
    {
        var definition = (CollectionDefinitionAttribute)Assert.Single(
            typeof(WpfStaIsolationCollection).GetCustomAttributes(
                typeof(CollectionDefinitionAttribute), inherit: false));
        Assert.Equal("WpfStaIsolation", definition.Name);
        Assert.True(definition.DisableParallelization);

        string[] suites =
        [
            "FieldRegressionFixTests",
            "OperatorNavigationSingleGlyphM214Tests",
            "M2QuickStartBrowseTests",
            "IedTaskDialogVisualRegressionTests",
            "NativeFatFieldEvidenceRegressionTests",
            "PremiumSmartIpWizardTests",
            "SsotV2LargeGridSearchSortBudgetTests",
            "MultiGridNaturalSortRegressionTests",
            "ProductionFatM2PermanentHostRegressionTests"
        ];
        foreach (var name in suites)
        {
            var type = typeof(WpfStaIsolationCollection).Assembly.GetType(
                "ARSAS.Tests." + name);
            Assert.NotNull(type);
            var assigned = (CollectionAttribute)Assert.Single(
                type.GetCustomAttributes(typeof(CollectionAttribute),
                    inherit: false));
            Assert.Equal("WpfStaIsolation", assigned.Name);
        }
    }
}
