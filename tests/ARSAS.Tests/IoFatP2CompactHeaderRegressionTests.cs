namespace ARSAS.Tests;

public sealed class IoFatP2CompactHeaderRegressionTests
{
    [Fact]
    public void P2_ReusesP0PrimaryAndSecondaryHeaderHierarchy()
    {
        var p0 = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0BenchUx.cs"));

        Assert.Contains("ConfigureP0AdaptiveHeaderActions();", p0, StringComparison.Ordinal);
        Assert.Contains("ConfigureP2CompactHeader();", p0, StringComparison.Ordinal);
        Assert.Contains("_p0PrimaryHeaderActions", p0, StringComparison.Ordinal);
        Assert.Contains("_p0SecondaryHeaderActions", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, WorkspacePreviewToggle)", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, _timeSyncEvidenceButton)", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, _comtradeEvidenceButton)", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, _cleanSessionButton)", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, _clockSyncGlobalStatusText)", p0, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(element, _clockSyncEvidenceText)", p0, StringComparison.Ordinal);
    }

    [Fact]
    public void P2_CompactsSecondaryControlsWithoutHidingEvidenceDetail()
    {
        var header = File.ReadAllText(FindRepoFile("IoListTestingWindow.P2CompactHeader.cs"));
        var clock = File.ReadAllText(FindRepoFile("IoListTestingWindow.ClockSyncUx.cs"));
        var supplemental = File.ReadAllText(FindRepoFile("IoListTestingWindow.SupplementalEvidence.cs"));

        Assert.Contains("WorkspacePreviewToggle.Content = \"Preview\";", header, StringComparison.Ordinal);
        Assert.Contains("_cleanSessionButton.Content = \"Clean FAT\";", header, StringComparison.Ordinal);
        Assert.Contains("TextTrimming.CharacterEllipsis", header, StringComparison.Ordinal);
        Assert.Contains("button.MinWidth = 0;", header, StringComparison.Ordinal);

        Assert.Contains("Text = \"SNTP · —\"", clock, StringComparison.Ordinal);
        Assert.Contains("\"SNTP · ON\"", clock, StringComparison.Ordinal);
        Assert.Contains("\"SNTP · OFF\"", clock, StringComparison.Ordinal);
        Assert.Contains("Rep {snapshot.ReplyCount}", clock, StringComparison.Ordinal);
        Assert.Contains("BuildClockSyncEvidenceToolTip", clock, StringComparison.Ordinal);
        Assert.Contains("Binding: {binding}", clock, StringComparison.Ordinal);
        Assert.Contains("Client request seen: {snapshot.ClientRequestCount}", clock, StringComparison.Ordinal);
        Assert.Contains("Mode 4 reply sent: {snapshot.ReplyCount}", clock, StringComparison.Ordinal);

        Assert.Contains("CreateEvidenceButton(\"Sync · —\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("CreateEvidenceButton(\"Clean FAT\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("$\"Sync · {timeSync.Verdict}\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("timeSync.DisplayText", supplemental, StringComparison.Ordinal);
        Assert.Contains("timeSync.Reason", supplemental, StringComparison.Ordinal);
        Assert.Contains("ied.LatestComtradeRemotePath", supplemental, StringComparison.Ordinal);
    }

    [Fact]
    public void P2_IsPresentationOnlyAndDoesNotTouchFatProtocolEvidenceOrGridSchema()
    {
        var header = File.ReadAllText(FindRepoFile("IoListTestingWindow.P2CompactHeader.cs"));

        Assert.DoesNotContain("SetVirtualizationMode", header, StringComparison.Ordinal);
        Assert.DoesNotContain("SetIsVirtualizing", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadObject", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync", header, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureCurrentEvidence", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleSave", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ARIEC61850", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grid.Columns", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ON RELAY TIME", header, StringComparison.Ordinal);
        Assert.DoesNotContain("OFF RELAY TIME", header, StringComparison.Ordinal);
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