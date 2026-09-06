namespace ARSAS.Tests;

public sealed class IoFatP1FirstPaintRegressionTests
{
    [Fact]
    public void P1_InstallsExistingFatV2SchemaOnLoadedBeforeFirstVisibleRender()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P1FirstPaint.cs"));

        Assert.Contains("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
        Assert.Contains("window.InstallFatV2WorkspaceUx();", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(e.OriginalSource, window)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentRendered", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_V2InstallerIsIdempotentAndDefinesOnlyApprovedEvidenceColumns()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("if (!_fatV2UxInstalled)", source, StringComparison.Ordinal);
        Assert.Contains("_fatV2UxInstalled = true;", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"LIVE VALUE\"", source, StringComparison.Ordinal);
        Assert.Contains("FatValueSlot.Value1", source, StringComparison.Ordinal);
        Assert.Contains("FatValueSlot.Value2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ON RELAY TIME\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OFF RELAY TIME\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_FirstPaintGuardDoesNotTouchVirtualizationProtocolOrEvidence()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P1FirstPaint.cs"));

        Assert.DoesNotContain("SetVirtualizationMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetIsVirtualizing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadObject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureCurrentEvidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ARIEC61850", source, StringComparison.OrdinalIgnoreCase);
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
