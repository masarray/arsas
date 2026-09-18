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

        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assets/Fonts/Inter-Regular.ttf"] = "40d692fce188e4471e2b3cba937be967878f631ad3ebbbdcd587687c7ebe0c82",
            ["Assets/Fonts/Inter-Medium.ttf"] = "97ad806f526e41546d46365bb3a393145f75b7b1568913db74549ad8b8dba872",
            ["Assets/Fonts/Inter-SemiBold.ttf"] = "78a843fade9d4612a5567302fb595b56976eb5fcebf4fea5a5912d638bafcde3",
            ["Assets/Fonts/Inter-Bold.ttf"] = "288316099b1e0a47a4716d159098005eef7c0066921f34e3200393dbdb01947f"
        };

        foreach (var (relativePath, expectedHash) in expectedHashes)
        {
            var fontPath = FindRepoFile(relativePath);
            var font = new FileInfo(fontPath);
            Assert.True(font.Length > 100_000, $"{relativePath} is missing or implausibly small.");

            var actualHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fontPath)))
                .ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
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

    [Fact]
    public void WorkstationWindows_DoNotReintroduceSystemFontFallbacksOrDisplayMetrics()
    {
        var presentationFiles = new[]
        {
            "ComtradeWorkspaceWindow.xaml",
            "ControlCommandWindow.xaml",
            "DynamicReportQualificationResultWindow.xaml",
            "FaultRecordWindow.xaml",
            "IoListTestingWindow.xaml",
            "IpConnectWizardWindow.xaml",
            "MainWindow.xaml",
            "RcbExportFilterWindow.xaml",
            "SaveSclWindow.xaml",
            "SclSignalSelectionModeWindow.xaml",
            "SignalSelectionWizardWindow.xaml",
            "SmvViewerWindow.xaml",
            "UpdatePromptWindow.xaml",
            "Resources/P2BlueSteelGreige.xaml"
        };

        foreach (var relativePath in presentationFiles)
        {
            var xaml = File.ReadAllText(FindRepoFile(relativePath));
            Assert.DoesNotContain("Aptos", xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FontFamily=\"Inter,", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("TextFormattingMode\" Value=\"Display\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("TextFormattingMode=\"Display\"", xaml, StringComparison.Ordinal);
        }

        var fat = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));
        Assert.Contains("FontFamily=\"{StaticResource AppFontFamily}\"", fat, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextRenderingMode=\"ClearType\"", fat, StringComparison.Ordinal);
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
