namespace ARSAS.Tests;

public sealed class IoListFatFieldUxRegressionTests
{
    [Fact]
    public void FatCommandDefaults_EnableInterlockAndSynchronismOnlyOnFirstAttach()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatFieldUx.cs"));

        Assert.Contains("_fatFieldDefaultsInitialized.Add(signal)", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlInterlockCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticAnalogValuePair_DoesNotExposeNormalCaptureButtons()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var capture = File.ReadAllText(FindRepoFile("Services/IoTesting/FatAutoCaptureCoordinator.cs"));

        Assert.Contains("nameof(IoTestPointPlan.IsOperatorSnapshot)", ux, StringComparison.Ordinal);
        Assert.Contains("CanCaptureOperatorSnapshot", ux, StringComparison.Ordinal);
        Assert.Contains("AnalogStableSampleCount = 3", capture, StringComparison.Ordinal);
        Assert.Contains("Stable analog Value 1 captured", capture, StringComparison.Ordinal);
        Assert.Contains("Stable analog Value 2 captured", capture, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandFailure_UsesProminentAutoDismissShoutWithReason()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatFieldUx.cs"));

        Assert.Contains("COMMAND FAILED", source, StringComparison.Ordinal);
        Assert.Contains("PlacementMode.Top", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeFatCommandFailureReason", source, StringComparison.Ordinal);
        Assert.Contains("_fatCommandFailureShout.IsOpen = true", source, StringComparison.Ordinal);
        Assert.Contains("_fatCommandFailureShout.IsOpen = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StopButton_IsMovedToReservedColumnWithoutReplacingItsExistingHandler()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatFieldUx.cs"));

        Assert.Contains("actionPanel.Children.Remove(stop);", source, StringComparison.Ordinal);
        Assert.Contains("headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(stop, 2);", source, StringComparison.Ordinal);
        Assert.Contains("headerGrid.Children.Add(stop);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stop.Click +=", source, StringComparison.Ordinal);
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
