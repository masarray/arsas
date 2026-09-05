using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// Scroll stability for the FAT v2 grid.
///
/// WPF does not allow VirtualizingPanel.VirtualizationMode to be changed after the
/// ItemsHost has entered Measure. The previous Loaded-class-handler implementation was
/// therefore invalid and could tear down the FAT render path with InvalidOperationException.
///
/// Apply the narrow virtualization policy during Window initialization, before the first
/// layout pass: keep row virtualization enabled, use Standard (non-recycling) containers,
/// and disable column virtualization for the small fixed FAT column set.
///
/// Deliberately no live-point subscriptions, Dispatcher work, MMS reads, RCB/DataSet
/// mutation, ItemsSource/filter mutation, session mutation, or evidence mutation occur here.
/// </summary>
public partial class IoListTestingWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        ApplyFatScrollStabilityBeforeFirstMeasure();
    }

    private void ApplyFatScrollStabilityBeforeFirstMeasure()
    {
        var grid = _fatSignalsGrid ?? FindVisualDescendant<DataGrid>(this);
        if (grid == null)
            return;

        // Fail closed rather than ever changing VirtualizationMode after WPF has measured
        // the ItemsHost. OnInitialized is expected to run before this point; this guard turns
        // any future lifecycle drift into "no scroll patch" instead of an application-wide
        // UI exception.
        if (grid.IsMeasureValid)
            return;

        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = false;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Standard);
    }
}
