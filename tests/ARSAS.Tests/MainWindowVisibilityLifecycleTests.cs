namespace ARSAS.Tests;

public sealed class MainWindowVisibilityLifecycleTests
{
    [Fact]
    public void MainWindow_ShowGuard_DoesNotReshowOwnerDuringApplicationShutdown()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SafeVisibilityLifecycle.cs"));
        var hostSource = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("public new void Show()", source, StringComparison.Ordinal);
        Assert.Contains("_shutdownStarted", source, StringComparison.Ordinal);
        Assert.Contains("_allowClose", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.HasShutdownStarted", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.HasShutdownFinished", source, StringComparison.Ordinal);
        Assert.Contains("base.Show();", source, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException) when (IsApplicationWindowShutdownInProgress())", source, StringComparison.Ordinal);

        Assert.Contains("void WindowClosed", hostSource, StringComparison.Ordinal);
        Assert.Contains("Show();", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("base.Show();", hostSource, StringComparison.Ordinal);
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
