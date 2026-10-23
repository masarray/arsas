using ArIED61850Tester;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class RcbSelectionVirtualizationRegressionTests
{
    [Fact]
    public void LegacyRcbGrid_RecycledCheckboxEventsCannotReachSingleSelectHandlers()
    {
        var xaml = Read("RcbExportFilterWindow.xaml");
        var authority = Read("RcbExportFilterWindow.VirtualizedSelectionAuthority.cs");

        Assert.Contains(
            "IsChecked=\"{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"",
            xaml,
            StringComparison.Ordinal);

        // These legacy handlers still exist for the original single-RCB window contract. The
        // P1 multi-select authority must stop virtualization-generated Checked/Unchecked events
        // before those instance handlers can translate visual rehydration into SelectOnly().
        Assert.Contains("Checked=\"RcbCheckBox_Checked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"RcbCheckBox_Unchecked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.CheckedEvent", authority, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.UncheckedEvent", authority, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(grid, window.RcbGrid)", authority, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiRcbSelectionAuthority_KeepsIndependentRowsSelected()
    {
        var rows = new List<RcbExportRow>
        {
            NewRow("Buffer"),
            NewRow("Unbuffer"),
            NewRow("A_URCB"),
            NewRow("A_URCB_1"),
            NewRow("A_URCB_10")
        };
        var anchorIndex = -1;
        var anchorValue = false;

        for (var index = 0; index < 4; index++)
        {
            MainWindow.ApplyRcbSelectionForTest(
                rows,
                ref anchorIndex,
                ref anchorValue,
                index,
                extendRange: false);
        }

        Assert.Equal(4, rows.Count(row => row.IsSelected));
        Assert.All(rows.Take(4), row => Assert.True(row.IsSelected));
        Assert.False(rows[4].IsSelected);
    }

    private static RcbExportRow NewRow(string name)
        => new()
        {
            Name = name,
            Reference = $"IED/LLN0.{name}",
            SourceSelectionKey = name,
            ExportName = name
        };

    private static string Read(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
