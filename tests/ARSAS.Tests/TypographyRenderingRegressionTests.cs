namespace ARSAS.Tests;

public sealed class TypographyRenderingRegressionTests
{
    [Fact]
    public void EmbeddedInterFaces_ArePackagedAsWpfResourcesAndLicensed()
    {
        var project = File.ReadAllText(FindRepoFile("ArIED61850Tester.csproj"));

        Assert.Contains("<Resource Include=\"Assets\\Fonts\\Inter-Regular.ttf\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\Fonts\\Inter-Medium.ttf\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\Fonts\\Inter-SemiBold.ttf\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\Fonts\\Inter-Bold.ttf\" />", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\Fonts\\Inter-LICENSE.txt", project, StringComparison.Ordinal);
        Assert.Contains("THIRD_PARTY\\Inter-LICENSE.txt", project, StringComparison.Ordinal);

        foreach (var relativePath in new[]
                 {
                     "Assets/Fonts/Inter-Regular.ttf",
                     "Assets/Fonts/Inter-Medium.ttf",
                     "Assets/Fonts/Inter-SemiBold.ttf",
                     "Assets/Fonts/Inter-Bold.ttf"
                 })
        {
            var font = new FileInfo(FindRepoFile(relativePath));
            Assert.True(font.Length > 100_000, $"{relativePath} is missing or implausibly small.");
        }

        var license = File.ReadAllText(FindRepoFile("Assets/Fonts/Inter-LICENSE.txt"));
        Assert.Contains("SIL OPEN FONT LICENSE", license, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedTypography_UsesInterWithSmoothStaticTextRendering()
    {
        var app = File.ReadAllText(FindRepoFile("App.xaml"));

        Assert.Contains("<FontFamily x:Key=\"AppFontFamily\">./Assets/Fonts/#Inter</FontFamily>", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"TextOptions.TextFormattingMode\" Value=\"Ideal\"/>", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"TextOptions.TextRenderingMode\" Value=\"ClearType\"/>", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"TextOptions.TextHintingMode\" Value=\"Fixed\"/>", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"UseLayoutRounding\" Value=\"True\"/>", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"SnapsToDevicePixels\" Value=\"True\"/>", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TextOptions.TextFormattingMode\" Value=\"Display\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkstation_DoesNotFallBackToAptosTypography()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("FontFamily=\"{StaticResource AppFontFamily}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextFormattingMode=\"Ideal\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextRenderingMode=\"ClearType\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextHintingMode=\"Fixed\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Aptos", mainWindow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TextOptions.TextFormattingMode=\"Display\"", mainWindow, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
