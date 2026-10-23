using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217QStatusAndExplorerRegressionTests
{
    [Theory]
    [InlineData("BCUGEF650/CSWI1.Mod.stVal", "value1", "on")]
    [InlineData("BCUGEF650/CSWI1.Beh.stVal", "1", "on")]
    [InlineData("BCUGEF650/CSWI1.Health.stVal", "value1", "Ok")]
    [InlineData("BCUGEF650/CSWI1.Mod.stVal", "value5", "off")]
    [InlineData("BCUGEF650/CSWI1.Health.stVal", "value2", "warning")]
    public void StandardEnumsRenderByExactObservedFcAndDo(string reference, string raw, string expected)
        => Assert.Equal(expected,
            IedBrowserStandardStatusPresentation.Format(reference, "ST", raw));

    [Theory]
    [InlineData("BCUGEF650/CSWI1.FltNum.stVal", "value1", "ST")]
    [InlineData("BCUGEF650/CSWI1.Mod.stVal", "value1", "MX")]
    [InlineData("BCUGEF650/CSWI1.Mod.q", "value1", "ST")]
    [InlineData("BCUGEF650/CSWI10.Mod.stVal", "value1", "SP")]
    public void UnrelatedReferencesAndFunctionalConstraintsNeverRewriteWireEvidence(
        string reference, string raw, string fc)
        => Assert.Equal(raw, IedBrowserStandardStatusPresentation.Format(reference, fc, raw));

    [Fact]
    public void PersistentExplorerMustOccupyAvailableStarRowWhileDockIsIndependent()
    {
        var root = FindRoot();
        var shell = File.ReadAllText(Path.Combine(root, "MainWindow.PersistentWorkbench.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));
        Assert.Contains("legacyExplorerLayout.MinHeight = 0;", shell);
        Assert.Contains("MainTabs.MinHeight = 0;", shell);
        Assert.DoesNotContain("legacyExplorerLayout.MinHeight = viewport;", shell);
        Assert.Contains("MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;", shell);
        Assert.Contains("ExplorerCommandDockRow", xaml);
        Assert.Contains("Grid.Row=\"2\" ItemsSource=\"{Binding SelectedDevice.Points}\"", xaml);
        Assert.Contains("Height=\"*\"/>", xaml);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate MainWindow.xaml.");
    }
}
