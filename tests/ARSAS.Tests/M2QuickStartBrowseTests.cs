using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Source-neutral task authority contracts for imported SCL and discovered IEDs.
/// The actual MMS and report paths remain covered by existing integration suites.
/// </summary>
public sealed class M2QuickStartBrowseTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void QuickStart_ExposesTwoPrimaryChoices_AndKeepsManualAsSecondary()
    {
        var document = XDocument.Load(Find("SclSignalSelectionModeWindow.xaml"));
        var buttons = document.Descendants(P + "Button").ToArray();
        XElement Action(string handler) => Assert.Single(buttons,
            button => (string?)button.Attribute("Click") == handler);

        var report = Action("MonitorStaticDataSet_Click");
        var browse = Action("BrowseIed_Click");
        var manual = Action("MonitorManual_Click");
        Assert.Equal("{StaticResource TaskDialogPrimaryAction}",
            (string?)report.Attribute("Style"));
        Assert.Equal("{StaticResource TaskDialogPrimaryAction}",
            (string?)browse.Attribute("Style"));
        Assert.Equal("{StaticResource TaskDialogSecondaryAction}",
            (string?)manual.Attribute("Style"));
        Assert.Contains("Browse IED", browse.Value, StringComparison.Ordinal);
        Assert.Contains("Explore model and live values", browse.Value, StringComparison.Ordinal);
        Assert.Equal("1", (string?)manual.Attribute("Grid.Row"));
        Assert.Contains("Click=\"BrowseIed_Click\"", File.ReadAllText(Find(
            "SclSignalSelectionModeWindow.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void TypedTaskSelection_CannotConflateBrowseAndStaticOrCancellation()
    {
        Assert.NotEqual(IedWorkspaceTaskChoice.None, IedWorkspaceTaskChoice.BrowseIed);
        Assert.NotEqual(IedWorkspaceTaskChoice.StaticDataSet, IedWorkspaceTaskChoice.BrowseIed);
        Assert.NotEqual(IedWorkspaceTaskChoice.BrowseIed, IedWorkspaceTaskChoice.ManualSignals);
        var dialog = File.ReadAllText(Find("SclSignalSelectionModeWindow.xaml.cs"));
        Assert.Contains("Selection { get; private set; } = IedWorkspaceTaskChoice.None", dialog,
            StringComparison.Ordinal);
        Assert.Contains("Selection = IedWorkspaceTaskChoice.BrowseIed;", dialog,
            StringComparison.Ordinal);
        Assert.Contains("Selection = IedWorkspaceTaskChoice.StaticDataSet;", dialog,
            StringComparison.Ordinal);
        Assert.Contains("Selection = IedWorkspaceTaskChoice.ManualSignals;", dialog,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_useStaticDataSet", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void BothSourcePaths_RouteBrowseWithoutStartingMonitoringOrEditingFat()
    {
        var scl = File.ReadAllText(Find("MainWindow.xaml.cs"));
        var shared = File.ReadAllText(Find("MainWindow.SharedSclWorkspace.cs"));
        var card = File.ReadAllText(Find("MainWindow.SclQuickActions.cs"));
        Assert.Contains("IedWorkspaceTaskChoice.BrowseIed => SclSignalSelectionMode.BrowseIed",
            shared, StringComparison.Ordinal);
        Assert.Contains("else if (selectionMode == SclSignalSelectionMode.BrowseIed)",
            scl, StringComparison.Ordinal);
        Assert.Contains("OpenIedBrowserModel(firstImported);", scl, StringComparison.Ordinal);
        Assert.Contains("discoveredLdCount > 0", scl, StringComparison.Ordinal);
        Assert.Contains("device.Signals.Count > 0 ||", scl, StringComparison.Ordinal);
        Assert.Contains("sharedSelectionMode is SclSignalSelectionMode.StaticDataSet or SclSignalSelectionMode.Manual",
            scl, StringComparison.Ordinal);

        var cardBrowse = card.IndexOf("if (dialog.Selection == IedWorkspaceTaskChoice.BrowseIed)",
            StringComparison.Ordinal);
        var staticTask = card.IndexOf("if (dialog.Selection == IedWorkspaceTaskChoice.StaticDataSet)",
            StringComparison.Ordinal);
        Assert.True(cardBrowse >= 0 && cardBrowse < staticTask);
        var browseBranch = card[cardBrowse..staticTask];
        Assert.Contains("OpenIedBrowserModel(device);", browseBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync(", browseBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("UseHybrid(", browseBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyStaticDataSetSelection(", browseBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync(", browseBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseLanding_UsesLazyFirstLdLn_AndKeepsNetworkReadExplicit()
    {
        var source = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        var start = source.IndexOf("internal void OpenIedBrowserModel(", StringComparison.Ordinal);
        var end = source.IndexOf("internal void OpenIedBrowserRcb(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var route = source[start..end];
        Assert.Contains("FindVisible(\"category/model\")", route, StringComparison.Ordinal);
        Assert.Contains("_iedBrowserTree.Expand(category);", route, StringComparison.Ordinal);
        Assert.Contains("var firstNode = ld?.GetChildren().FirstOrDefault();", route,
            StringComparison.Ordinal);
        Assert.Contains("RenderIedBrowserSelection(target,", route, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync(", route, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingCachedModelAsync(", route, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync(", route, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateRcb(", route, StringComparison.Ordinal);
        Assert.Contains("IedBrowserReadValues_Click", File.ReadAllText(Find("MainWindow.xaml")),
            StringComparison.Ordinal);
        Assert.Contains("connected ? \"Read\" : \"Connect & Read\"", source,
            StringComparison.Ordinal);
        Assert.Contains("hasModel && canReachIED && _iedBrowserReadOperation == null", source,
            StringComparison.Ordinal);
        Assert.Contains("await _runtime.ConnectUsingCachedModelAsync(device, cts.Token)", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeWpfQuickStart_RealButtonClicksDispatchDistinctTypedTasks()
    {
        if (!OperatingSystem.IsWindows()) return;

        foreach (var (buttonName, expected) in new[]
        {
            ("BrowseIedActionButton", IedWorkspaceTaskChoice.BrowseIed),
            ("StaticDataSetActionButton", IedWorkspaceTaskChoice.StaticDataSet),
            ("ManualSignalsActionButton", IedWorkspaceTaskChoice.ManualSignals)
        })
        {
            Exception? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var dialog = new ArIED61850Tester.SclSignalSelectionModeWindow(1);
                    dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() =>
                        {
                            var button = Assert.IsType<System.Windows.Controls.Button>(
                                dialog.FindName(buttonName));
                            button.RaiseEvent(new System.Windows.RoutedEventArgs(
                                System.Windows.Controls.Button.ClickEvent, button));
                        }));
                    Assert.True(dialog.ShowDialog() == true);
                    Assert.Equal(expected, dialog.Selection);
                    dialog.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)),
                "Native Quick Start click timed out: " + buttonName);
            Assert.Null(failure);
        }
    }

    private static string Find(string filename)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, filename);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(filename);
    }
}
