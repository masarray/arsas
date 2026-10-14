using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserReadResultInboxTests
{
    private static IedBrowserReadResult Ok(int id) => new(
        new IedBrowserReadTarget("LD/MMXU1.Hz.mag.f" + id, "MX", "FLOAT32"),
        id.ToString(System.Globalization.CultureInfo.InvariantCulture), "", "", "");

    [Fact]
    public void ResultsArrivingBeforeAsyncCompletionAreDeliveredByFinalSynchronousDrain()
    {
        var scheduled = 0;
        var inbox = new IedBrowserReadResultInbox(() => Interlocked.Increment(ref scheduled));
        for (var i = 0; i < 125; i++)
            inbox.Report(Ok(i));
        // Simulate an idle UI dispatcher: no queued notification has executed.
        Assert.Equal(1, scheduled);
        var accepted = new List<string>();
        Assert.Equal(125, inbox.Drain(r => accepted.Add(r.Value)));
        Assert.Equal(125, accepted.Count);
        Assert.Equal("0", accepted[0]);
        Assert.Equal("124", accepted[^1]);
        Assert.Equal(0, inbox.Drain(_ => throw new Exception("already delivered")));
    }

    [Fact]
    public async Task ConcurrentProducersNeverLoseOrDuplicateResults()
    {
        var scheduled = 0;
        var inbox = new IedBrowserReadResultInbox(() => Interlocked.Increment(ref scheduled));
        await Task.WhenAll(Enumerable.Range(0, 6).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                inbox.Report(Ok(worker * 100 + i));
        })));
        var ids = new HashSet<int>();
        Assert.Equal(600, inbox.Drain(result =>
            Assert.True(ids.Add(int.Parse(result.Value,
                System.Globalization.CultureInfo.InvariantCulture)))));
        Assert.Equal(600, ids.Count);
        Assert.Equal(0, inbox.Drain(_ => throw new Exception("duplicate")));
        Assert.InRange(scheduled, 1, 1);
    }

    [Fact]
    public void ResultPublishedDuringDrainSchedulesAnotherBatchWithoutLostWakeup()
    {
        var notifications = 0;
        var inbox = new IedBrowserReadResultInbox(() => notifications++);
        inbox.Report(Ok(1));
        var delivered = new List<string>();
        inbox.Drain(result =>
        {
            delivered.Add(result.Value);
            if (result.Value == "1") inbox.Report(Ok(2));
        });
        Assert.Equal(["1", "2"], delivered);
        inbox.Report(Ok(3));
        Assert.Equal(2, notifications);
        Assert.Equal(1, inbox.Drain(result => delivered.Add(result.Value)));
        Assert.Equal(["1", "2", "3"], delivered);
    }

    [Fact]
    public void UiReadHandlerUsesExplicitInboxCompletionBeforeResultsSubtitle()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("new IedBrowserReadResultInbox(", source);
        Assert.DoesNotContain("new Progress<IedBrowserReadResult>", source);
        var finalDrain = source.IndexOf("DrainReadResults();\n            if (!cts.IsCancellationRequested", StringComparison.Ordinal);
        var summary = source.IndexOf("Read complete ·", StringComparison.Ordinal);
        Assert.True(finalDrain >= 0 && summary > finalDrain);
        Assert.Contains("sourceDevice.LiveValues.Association != readAssociation", source);
    }
}
