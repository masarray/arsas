using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    internal void NotifySharedWorkspacePointsChanged(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);

        // TestPoints is intentionally persisted as a List. Manual SCL selection can add a
        // row while FAT is hidden in Engineering, so force the existing collection view to
        // rebuild when the operator returns instead of requiring a second import/window.
        RefreshFatV2WorkspaceUx(refreshRows: true);
        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
        RaiseSelectedIedContextProperties();
    }
}
