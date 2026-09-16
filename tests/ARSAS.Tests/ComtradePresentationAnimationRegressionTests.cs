namespace ARSAS.Tests;

public sealed class ComtradePresentationAnimationRegressionTests
{
    [Fact]
    public void PresentationViews_PreserveFrameClock_AndReuseHarmonicBuffers()
    {
        var phasor = File.ReadAllText(FindRepoFile("Controls/ComtradePhasorView.cs"));
        var harmonic = File.ReadAllText(FindRepoFile("Controls/ComtradeHarmonicsWorkstationView.cs"));

        // Retargeting restarts the settle deadline without resetting an already-running frame clock.
        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", phasor, StringComparison.Ordinal);
        Assert.Contains("_presentationAnimationStartedTimestamp = now;", phasor, StringComparison.Ordinal);
        Assert.Contains("if (_presentationRenderingHooked) return;", phasor, StringComparison.Ordinal);

        // Harmonic composition frames mutate reusable numeric buffers; static assets stay cached.
        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", harmonic, StringComparison.Ordinal);
        Assert.Contains("AdvancePreparedRows(_preparedRows, elapsedMilliseconds)", harmonic, StringComparison.Ordinal);
        Assert.Contains("TargetMagnitudes", harmonic, StringComparison.Ordinal);
        Assert.Contains("TargetMagnitudeLabels", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshPreparedSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("SmoothSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("_smoothedSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", phasor, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", harmonic, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeScrub_RemainsLatestWinsSingleWorker()
    {
        var source = File.ReadAllText(FindRepoFile("ComtradeWorkspaceWindow.P1D4LiveScrub.cs"));
        Assert.Contains("if (_p1d4ScrubWorkerRunning || !_p1d4ScrubDirty)", source, StringComparison.Ordinal);
        Assert.Contains("_p1d4ScrubWorkerRunning = true", source, StringComparison.Ordinal);
        Assert.Contains("if (_p1d4ScrubDirty && _analysisMode != AnalysisMode.Waveform)", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
