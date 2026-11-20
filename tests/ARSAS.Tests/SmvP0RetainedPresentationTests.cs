using System.IO;
using ArIED61850Tester.Views;
using ProcessBus.App.Wpf.Controls;
using ProcessBus.Core.Models;

namespace ARSAS.Tests;

public sealed class SmvP0RetainedPresentationTests
{
    [Theory]
    [InlineData(-120, 240)]
    [InlineData(-160, 200)]
    [InlineData(-140, 220)]
    [InlineData(-0.001, 0)]
    [InlineData(360, 0)]
    [InlineData(720, 0)]
    [InlineData(100, 100)]
    [InlineData(375, 15)]
    public void Normalize360_AffectsPresentationOnly(double input, double expected)
    {
        Assert.Equal(expected, SmvAnglePresentation.Normalize360(input), 5);
    }

    [Fact]
    public void AnglesInGridAndPhasorLabel_ShareSameUnsignedConvention()
    {
        var voltage = new SmvDisplayMeasurement(6, "Ub", "V", 49.999,
            49.999, -160, true);
        Assert.Equal("∠200.0°", voltage.AngleText);
        Assert.Equal("Ub 50V ∠200°", PhasorDiagramControl.FormatPhasorLabel(
            new PhasorDisplayItem
            {
                Name = "Ub", Unit = "V", Family = PhasorFamily.Voltage,
                Magnitude = 49.999, AngleDegrees = -160
            }));
        Assert.Equal("Ia 1.002A ∠15°", PhasorDiagramControl.FormatPhasorLabel(
            new PhasorDisplayItem
            {
                Name = "Ia", Unit = "A", Family = PhasorFamily.Current,
                Magnitude = 1.002, AngleDegrees = 15
            }));
        Assert.Equal(-160, voltage.PhaseDegrees);
    }

    [Fact]
    public void LiveMeasurementRow_PreservesIdentityWhileNotifyingOnlyChangedCells()
    {
        var old = new SmvDisplayMeasurement(1, "Ia", "A", 1.0, 1.0, -120, true);
        var row = new SmvMeasurementLiveRow(old);
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        row.Apply(new SmvDisplayMeasurement(1, "Ia", "A", 1.002, 1.002, 240, true));
        Assert.Equal("1.002 A", row.RmsText);
        Assert.Equal("∠240.0°", row.AngleText);
        Assert.Contains(nameof(row.RmsText), changed);
        Assert.DoesNotContain(nameof(row.DisplayName), changed);
        // -120 and +240 are the SAME displayed phase; no text disturbance.
        Assert.DoesNotContain(nameof(row.AngleText), changed);
        changed.Clear();
        row.Apply(new SmvDisplayMeasurement(1, "Ia", "A", 1.002, 1.002, 240, true));
        Assert.Empty(changed);
        Assert.Throws<InvalidOperationException>(() => row.Apply(
            new SmvDisplayMeasurement(2, "Ib", "A", 1, 1, 0, true)));
    }

    [Fact]
    public void StreamTransition_RetainsLastCoherentFrame_AndNeverShowsTemporaryUnknown()
    {
        var code = Read("Views/SmvInlineViewer.xaml.cs");
        var selectionStart = code.IndexOf("private void Stream_SelectionChanged", StringComparison.Ordinal);
        var selectionEnd = code.IndexOf("private void QueueUpdate", selectionStart, StringComparison.Ordinal);
        var selection = code[selectionStart..selectionEnd];
        Assert.DoesNotContain("ClearPresentation(stream is null ?", selection);
        Assert.Contains("_pendingAnalysis = null;", selection);
        Assert.Contains("_awaitingPublisherSwitch = _visibleSnapshot is not null;", selection);
        Assert.Contains("Notify(nameof(SyncDetail));", selection);
        Assert.Contains("_lastDisplayedPublisherKey", code);
        Assert.Contains("CurrentStreamKey = _lastDisplayedPublisherKey;", code);
        Assert.Contains("Streams.Move(existing, i);", code);
        Assert.Contains("Measurements[rowIndex].Apply(next);", code);
        Assert.DoesNotContain("Measurements[rowIndex] = next;", code);
    }

    [Fact]
    public void RawProfile_CollapsesEntirePowerCard_InsteadOfPresentingDashes()
    {
        var xaml = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("Grid.Row=\"4\" Visibility=\"{Binding PowerVisibility}\"", xaml);
        var code = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("public Visibility PowerVisibility => ActiveUnits == SmvDisplayUnits.NineTwoLe && _powerTotal is not null", code);
        Assert.Contains("Notify(nameof(PowerVisibility));", code);
    }

    private static string Read(string relativePath)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(relativePath);
    }
}
