using System.IO;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P0PostWriteIndependentCswiRecoveryTests
{
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void IndependentBkrCswiIsNotCoveredByTheOldDistinctSelectedSweep()
    {
        // The exact operator case: verified CSWI outside the selected static
        // DataSet. A DistinctSelected post-dialog sweep misses this model.
        var independent = new SignalDefinition
        {
            Name = "BkrCSWI1.Pos",
            ObjectReference = "SG7_BCUSystem/BkrCSWI1.Pos",
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            ControlCdc = "DPC",
            IsSelected = false
        };
        Assert.DoesNotContain(independent,
            Iec61850ControlIdentity.DistinctSelected(new[] { independent }));
    }

    [Fact]
    public void PostWriteTargetsTheActualDialogSignalWithoutFullMmsRescan()
    {
        var main = Source("MainWindow.xaml.cs");
        var start = main.IndexOf("private async void ControlDetails_Click(", StringComparison.Ordinal);
        var end = main.IndexOf("private void RemoveControlFeedbackIndex(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var flow = main[start..end];

        Assert.Contains("if (dialog.WriteAttempted)", flow);
        Assert.Contains("await ReinspectConfiguredControlAsync(device, signal, dialog.WriteAssociation);", flow);
        Assert.DoesNotContain("await RefreshControlValuesAsync(device, force: true);", flow);
        Assert.Contains("writeAssociation != device.LiveValues.Association", flow);
        Assert.Contains("!device.Signals.Contains(signal)", flow);
        Assert.Contains("_runtime.InspectControlAsync(", flow);
        Assert.Contains("signal.ClearLiveControlAuthority();", flow);
        Assert.Contains("signal.ControlModelText = \"Verification required\";", flow);
        Assert.Contains("device.RefreshCommandSignalProjection();", flow);

        var native = Source("Services/NativeIec61850Client.cs");
        Assert.Contains("signal.ControlModelText = \"Auto-detect\";", native);
        Assert.Contains("await InvalidateCachedControlSessionAsync(signal.ObjectReference)", native);
    }

    [Fact]
    public void InitialWpfWindowWidensOnlyWithinScreenWorkAreaAndRemainsUserResizable()
    {
        var xaml = Source("MainWindow.xaml");
        var main = Source("MainWindow.xaml.cs");
        Assert.Contains("Height=\"960\" Width=\"1880\"", xaml);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\" WindowState=\"Normal\"", xaml);
        Assert.Contains("var workArea = SystemParameters.WorkArea;", main);
        Assert.Contains("Width = Math.Max(MinWidth, Math.Min(1880d, workArea.Width - 8d));", main);
        Assert.Contains("Height = Math.Max(MinHeight, Math.Min(960d, workArea.Height - 8d));", main);
    }
}
