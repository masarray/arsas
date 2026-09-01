using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatP53PartialLiveRegressionTests
{
    [Fact]
    public void Preparation_AllowsUsablePartialSclLiveScopeWithoutMutatingOperatorSelection()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("mayProceedWithPartialSclSelection", source, StringComparison.Ordinal);
        Assert.Contains("selection.Matches.Count > 0", source, StringComparison.Ordinal);
        Assert.Contains("unresolvedSelectionPoints.All(IoTestSignalSelectionService.IsSclDataSetAuthority)", source, StringComparison.Ordinal);
        Assert.Contains("if (liveCount == 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveFromFat()", source, StringComparison.Ordinal);
        Assert.Contains("checkbox or FAT disposition is changed by the engine", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartFat_ArmsOnlyLiveSubsetAndLeavesWaitingRowsSelected()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ContextUx.cs"));

        Assert.Contains("point.LiveBindingState == IoTestLiveBindingState.LivePointReady", source, StringComparison.Ordinal);
        Assert.Contains("Session.Start(selectedIed, liveCaptureScope)", source, StringComparison.Ordinal);
        Assert.Contains("waitingCount = captureScope.Count - liveCaptureScope.Count", source, StringComparison.Ordinal);
        Assert.Contains("checkbox/disposition unchanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveFromFat()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorSelectedWaitingRow_RemainsIncludedAndSelectedByModelContract()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "waiting-structured-member",
            IedName = "IED1",
            SignalName = "A phsA",
            ObjectReference = "IED1LD0/MMXU1.A.phsA",
            TestEnabled = true,
            ImportReady = true
        };

        point.ApplyLiveBinding(
            IoTestLiveBindingState.NotEvaluated,
            "Waiting for safe structured primary binding.",
            "device-1");

        Assert.True(point.TestEnabled);
        Assert.True(point.IsIncludedInFat);
        Assert.Equal(IoTestLiveBindingState.NotEvaluated, point.LiveBindingState);
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
