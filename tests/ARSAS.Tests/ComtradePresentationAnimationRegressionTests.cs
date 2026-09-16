namespace ARSAS.Tests;

public sealed class ComtradePresentationAnimationRegressionTests
{
    [Fact]
    public void PresentationViews_AnimateAtCompositionCadence_ThenSettleExactly()
    {
        var phasor = File.ReadAllText(FindRepoFile("Controls/ComtradePhasorView.cs"));
        var harmonic = File.ReadAllText(FindRepoFile("Controls/ComtradeHarmonicsWorkstationView.cs"));

        // Cursor/native analysis remains exact; only the derived presentation plane is eased.
        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", phasor, StringComparison.Ordinal);
        Assert.Contains("_smoothedVoltageVectors = CloneVectors(_targetVoltageVectors)", phasor, StringComparison.Ordinal);
        Assert.Contains("_smoothedCurrentVectors = CloneVectors(_targetCurrentVectors)", phasor, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", harmonic, StringComparison.Ordinal);
        Assert.Contains("_smoothedSpectra = CloneSpectra(_targetSpectra)", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", phasor, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", harmonic, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeScrub_RemainsLatestWinsSingleWorker()
    {
        var source = File.ReadAllText(FindRepoFile("ComtradeWorkspaceWindow.P1D4LiveScrub.cs"));
        Assert.Contains("if (_p1d4ScrubWorkerRunning || !_p1d4ScrubDirty)", source, StringComparison.Ordinal);
        Assert.Contains("_p1d4ScrubWorkerRunning = true", source, StringComparison.Ordinal);
        Assert.Contains("if (_p1d4ScrubDirty)", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
