namespace ARSAS.Tests;

public sealed class P0GrxCswiSelectionVisibilityTests
{
    [Fact]
    public void WizardMustOfferExplicitCswiCatalogFilterRatherThanAssumingXcbrOperability()
    {
        var xaml = ReadSource("SignalSelectionWizardWindow.xaml");
        var code = ReadSource("SignalSelectionWizardWindow.xaml.cs");
        Assert.Contains("x:Name=\"QuickCswiButton\"", xaml);
        Assert.Contains("Tag=\"CSWI\"", xaml);
        Assert.Contains("\"CSWI\" => IsCswiControl(signal)", code);
        Assert.Contains("QuickCswiButton.Content = $\"CSWI ({cswiCount:N0})\"", code);
        Assert.Contains("XCBR/XSWI status is not proof of a CSWI command object", code);
        Assert.Contains("QuickCswiButton, QuickSelectedButton", code);
        Assert.Contains("QuickCswiButton.IsChecked = false", code);
        // Safe: selecting an IED object is not authorizing a physical Operate.
        Assert.DoesNotContain("ExecuteControlAsync(", code);
    }

    private static string ReadSource(string relative)
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory);
             root is not null; root = root.Parent)
        {
            var p = Path.Combine(root.FullName, relative);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(relative);
    }
}
