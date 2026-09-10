from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def write(path, text):
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly 1 match, found {count}")
    return text.replace(old, new, 1)


# M2.1 - FAT is a permanent MainWindow host, never a launcher workflow.
path = "MainWindow.ProductionFatTab.cs"
s = read(path)
s = replace_once(
    s,
    "        _nativeFatTab.Content = BuildProductionFatLauncher();",
    "        _nativeFatTab.Content = BuildProductionFatPermanentHost();\n\n        // M2 prewarm starts as soon as the canonical host exists. It is deliberately\n        // independent of the currently selected tab so a valid Engineering SCL/DataSet\n        // can prepare the exact production surface before the operator first opens FAT.\n        QueueProductionFatEngineeringBootstrap();",
    "install permanent FAT host")

pattern = re.compile(
    r"    private FrameworkElement BuildProductionFatLauncher\(\)\n    \{.*?\n    \}\n\n    private void ProductionFat_MainTabsSelectionChanged",
    re.S,
)
replacement = '''    private FrameworkElement BuildProductionFatPermanentHost()
    {
        // This is a stable shell slot, not a launcher/form. With a valid Engineering
        // static DataSet it is replaced in the background by the exact production FAT
        // workspace before first navigation. With no eligible IED it remains a quiet
        // contextual empty state and never creates an alternate FAT workflow.
        var root = new Grid { Margin = new Thickness(0) };
        root.Children.Add(new TextBlock
        {
            Text = "FAT · awaiting an Engineering IED with static DataSet scope",
            FontSize = 12,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return root;
    }

    private void ProductionFat_MainTabsSelectionChanged'''
s, n = pattern.subn(replacement, s, count=1)
if n != 1:
    raise SystemExit(f"replace production launcher body: expected 1 match, found {n}")

s = replace_once(
    s,
    "        MainTabs.SelectedIndex = NativeFatWorkspaceIndex;\n        QueueNativeFatNavigationGeometry();\n\n        // The legacy launcher hides Engineering before showing IoListTestingWindow.\n        // Once its proven central workspace is re-parented here, restore Engineering and\n        // keep the legacy Window loaded-but-hidden solely as the production controller owner.\n        IsEnabled = true;\n        if (!IsVisible)\n            Show();\n        if (WindowState == WindowState.Minimized)\n            WindowState = WindowState.Normal;\n        Activate();\n\n        SetStatus($\"FAT ready in Engineering tab · {window.Project.Ieds.Count} IED · production auto-capture workflow.\");",
    "        // Passive mount: prewarming must never navigate, hide/show, activate, or steal\n        // focus from the operator's current Engineering destination.\n        window.RegisterEmbeddedHostCloseCleanup();\n        QueueNativeFatNavigationGeometry();\n\n        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)\n            SetStatus($\"FAT ready in Engineering tab · {window.Project.Ieds.Count} IED · production auto-capture workflow.\");",
    "passive production FAT mount")

s = replace_once(
    s,
    "            _nativeFatTab.Content = BuildProductionFatLauncher();",
    "            _nativeFatTab.Content = BuildProductionFatPermanentHost();",
    "unmount permanent FAT host")
write(path, s)


# M2.2 - Bootstrap is prewarm, not post-navigation work.
path = "MainWindow.ProductionFatEngineeringBootstrap.cs"
s = read(path)
s = replace_once(
    s,
    "        if (e.PropertyName != nameof(SelectedDevice) || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)\n            return;",
    "        if (e.PropertyName != nameof(SelectedDevice))\n            return;",
    "selected-device prewarm gate")
s = replace_once(
    s,
    "        if (!_productionFatEngineeringBootstrapInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)\n            return;",
    "        if (!_productionFatEngineeringBootstrapInstalled)\n            return;",
    "queue prewarm gate")
s = replace_once(
    s,
    "        if (_productionFatEngineeringBootstrapBusy ||\n            MainTabs.SelectedIndex != NativeFatWorkspaceIndex ||\n            !ProductionFatTabReady)",
    "        if (_productionFatEngineeringBootstrapBusy ||\n            !ProductionFatTabReady)",
    "ensure prewarm gate")
write(path, s)


# M2.3 - Establish static DataSet authority as an explicit prewarm trigger too. This
# covers same-IED SCL refreshes where SelectedDevice itself does not change.
path = "MainWindow.SharedSclWorkspace.cs"
s = read(path)
s = replace_once(
    s,
    "        _ = ObserveInitialStaticReportEvidenceAsync(device);",
    "        _ = ObserveInitialStaticReportEvidenceAsync(device);\n\n        // M2 permanent FAT host: authority establishment is also a readiness trigger.\n        // This covers SCL refresh on the same SelectedDevice, where PropertyChanged for\n        // SelectedDevice would otherwise not fire.\n        QueueProductionFatEngineeringBootstrap();",
    "static DataSet prewarm trigger")
write(path, s)


# M2.4 - Prepare the donor window before Show(), not from Loaded after first paint.
path = "IoListTestingWindow.EmbeddedEngineeringHost.cs"
s = read(path)
anchor = "    private static void EmbeddedEngineeringFatHost_Loaded(object sender, RoutedEventArgs e)\n"
method = '''    internal void PrepareForEmbeddedEngineeringHost()
    {
        // Must run before Window.Show(). A transparent/off-screen, non-activating donor
        // cannot produce the historical black/blank compositor frame while its exact
        // production center is being re-parented into MainWindow.
        ShowActivated = false;
        ShowInTaskbar = false;
        Opacity = 0d;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000d;
        Top = -32000d;
    }

'''
if method.strip() not in s:
    s = replace_once(s, anchor, method + anchor, "insert pre-show donor preparation")
s = replace_once(
    s,
    "        window.ShowActivated = false;\n        window.ShowInTaskbar = false;\n        window.Opacity = 0d;",
    "        window.PrepareForEmbeddedEngineeringHost();",
    "reuse donor preparation")
write(path, s)


# M2.5 - The production launch path prepares donor visibility before Show and does not
# hide MainWindow when the canonical embedded host exists.
path = "MainWindow.IoTesting.cs"
s = read(path)
s = replace_once(
    s,
    "        var window = new IoListTestingWindow(launch.Project, controller, persistence) { Owner = this };\n        RegisterLoadedIoFatWindow(window);",
    "        var window = new IoListTestingWindow(launch.Project, controller, persistence) { Owner = this };\n        if (ProductionFatTabReady)\n            window.PrepareForEmbeddedEngineeringHost();\n        RegisterLoadedIoFatWindow(window);",
    "prepare donor before Show")
s = replace_once(
    s,
    "        window.Closed += WindowClosed;\n        Hide();\n        window.Show();",
    "        window.Closed += WindowClosed;\n        if (!ProductionFatTabReady)\n            Hide();\n        window.Show();",
    "keep Engineering visible")
write(path, s)


# M2.6 - Keep old shadow-Hide safety net correct while it remains until M7 cleanup.
path = "MainWindow.ProductionFatNoFlicker.cs"
s = read(path)
s = replace_once(
    s,
    "        => _productionFatEngineeringBootstrapBusy &&\n           ProductionFatTabReady &&\n           MainTabs.SelectedIndex == NativeFatWorkspaceIndex;",
    "        => _productionFatEngineeringBootstrapBusy &&\n           ProductionFatTabReady;",
    "background prewarm no-flicker guard")
write(path, s)


# M2.7 - Update the existing architecture regression and add a focused permanent-host gate.
path = "tests/ARSAS.Tests/ProductionFatEngineeringTabRegressionTests.cs"
s = read(path)
s = replace_once(
    s,
    "        Assert.Contains(\"MainTabs.SelectedIndex != NativeFatWorkspaceIndex\", source, StringComparison.Ordinal);",
    "        Assert.Contains(\"QueueProductionFatEngineeringBootstrap();\", source, StringComparison.Ordinal);",
    "update bootstrap regression")
write(path, s)

new_test = ROOT / "tests/ARSAS.Tests/ProductionFatM2PermanentHostRegressionTests.cs"
new_test.write_text(r'''namespace ARSAS.Tests;

public sealed class ProductionFatM2PermanentHostRegressionTests
{
    [Fact]
    public void FatTab_IsPermanentHost_NotLauncherOrAlternateOpenSclWorkflow()
    {
        var source = Read("MainWindow.ProductionFatTab.cs");

        Assert.Contains("BuildProductionFatPermanentHost()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductionFatLauncher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open SCL for FAT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open ARSAS Project", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSclFatTesting_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenIoListPackage_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringStaticDataSet_PrewarmIsIndependentOfFatTabSelection()
    {
        var bootstrap = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");
        var shared = Read("MainWindow.SharedSclWorkspace.cs");

        Assert.Contains("e.PropertyName != nameof(SelectedDevice)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("QueueProductionFatEngineeringBootstrap();", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionFatEngineeringBootstrapBusy ||\n            MainTabs.SelectedIndex != NativeFatWorkspaceIndex", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionFatEngineeringBootstrapInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedDonor_IsPreparedBeforeShow_AndMainWindowStaysVisible()
    {
        var launch = Read("MainWindow.IoTesting.cs");
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");
        var tab = Read("MainWindow.ProductionFatTab.cs");

        var prepare = launch.IndexOf("window.PrepareForEmbeddedEngineeringHost();", StringComparison.Ordinal);
        var show = launch.IndexOf("window.Show();", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && show > prepare, "Donor visibility must be prepared before Window.Show().");
        Assert.Contains("if (!ProductionFatTabReady)\n            Hide();", launch, StringComparison.Ordinal);
        Assert.Contains("Left = -32000d", host, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0d", host, StringComparison.Ordinal);
        Assert.Contains("ShowActivated = false", host, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", host, StringComparison.Ordinal);

        Assert.DoesNotContain("MainTabs.SelectedIndex = NativeFatWorkspaceIndex", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Activate();", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsVisible)\n            Show();", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedSurface_RemainsExactProductionGridAndPreviewSource()
    {
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");

        Assert.Contains("InstallFatV2WorkspaceUx();", host, StringComparison.Ordinal);
        Assert.Contains("InstallPerIedPrintPreview();", host, StringComparison.Ordinal);
        Assert.Contains("DetachProductionFatCentralWorkspace()", host, StringComparison.Ordinal);
        Assert.Contains("owner.MountProductionFatWorkspace(this, surface)", host, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
''', encoding="utf-8")

print("M2 permanent FAT host migration applied successfully")
