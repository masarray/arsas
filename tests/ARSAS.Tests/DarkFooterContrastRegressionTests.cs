using System.Text.RegularExpressions;

namespace ARSAS.Tests;

public sealed class DarkFooterContrastRegressionTests
{
    [Fact]
    public void RcbExportFooter_UsesBrightTextOnDarkNavySurface()
    {
        var xaml = File.ReadAllText(FindRepoFile("RcbExportFilterWindow.xaml"));

        AssertFooterColor(xaml, "FooterSelectionSummaryText", "#F4F8FC");
        AssertFooterColor(xaml, "FooterRemovalSummaryText", "#93C5FD");
    }

    [Fact]
    public void SignalSelectionFooter_UsesBrightPrimarySecondaryAndSeparatorColors()
    {
        var xaml = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.xaml"));

        AssertFooterColor(xaml, "FooterSelectionCountText", "#F4F8FC");
        AssertFooterColor(xaml, "FooterVisibleCountText", "#C9D8E5");
        AssertNamedColor(xaml, "FooterSummarySeparator", "Background", "#8FA8BC");
    }

    private static void AssertFooterColor(string xaml, string name, string expectedColor)
        => AssertNamedColor(xaml, name, "Foreground", expectedColor);

    private static void AssertNamedColor(string xaml, string name, string property, string expectedColor)
    {
        var pattern = $@"x:Name=""{Regex.Escape(name)}""[\s\S]*?{Regex.Escape(property)}=""{Regex.Escape(expectedColor)}""";
        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), xaml);
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
