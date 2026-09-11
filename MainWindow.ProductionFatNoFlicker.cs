using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// P0 field fix for the embedded Engineering FAT transition.
///
/// The legacy production FAT launcher owns a historical MainWindow.Hide() / child Show()
/// hand-off. That is still required by standalone compatibility flows, but it is wrong for
/// the automatic Engineering -> embedded FAT path because the FAT tab is already visible
/// and the child Window exists only as a hidden controller/lifecycle owner. Suppress that
/// single hide while the automatic embedded bootstrap is in flight so the user never sees
/// the desktop/black frame between two WPF windows.
/// </summary>
public partial class MainWindow
{
    public new void Hide()
    {
        if (ShouldKeepEngineeringVisibleDuringProductionFatBootstrap())
            return;

        base.Hide();
    }

    private bool ShouldKeepEngineeringVisibleDuringProductionFatBootstrap()
        => _productionFatEngineeringBootstrapBusy &&
           ProductionFatTabReady &&
           MainTabs.SelectedIndex == NativeFatWorkspaceIndex;
}
