using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // Only an index of UI ROW IDENTITIES + exact IEC model dependency scopes.
    // Observations stay exclusively in device.LiveValues. Rebuilt when the
    // virtualized detail projection changes, never on a Report value update.
    private CanonicalBrowserViewportIndex? _iedBrowserViewportIndex;
    private Iec61850MonitorDevice? _iedBrowserViewportDevice;
    private IedBrowserTreeRow? _iedBrowserViewportPage;

    private void RebuildIedBrowserViewportIndex()
    {
        _iedBrowserViewportDevice = SelectedDevice;
        _iedBrowserViewportPage = _iedBrowserRenderedPage;
        _iedBrowserViewportIndex = new CanonicalBrowserViewportIndex(
            _iedBrowserDetails.Rows);
    }

    /// <summary>
    /// Returns null whenever the source/delta/viewport isn't fully provable:
    /// fallback to the original complete projection. An empty set means all
    /// changes are outside the current page, so no detail Value bindings fire.
    /// RCB/GOOSE/SGCB have independent ownership/TTL/control state and stay on
    /// the original refresh path regardless of the canonical ledger revision.
    /// </summary>
    private HashSet<IedBrowserDetailRow>? SelectIedBrowserReactiveRows(
        Iec61850MonitorDevice device, long newestVersion)
    {
        if (_iedBrowserLastLiveVersion < 0 ||
            _iedBrowserLastLiveVersion >= newestVersion ||
            !ReferenceEquals(_iedBrowserLastCfCanonicalModel, device.LiveCanonicalModel) ||
            !ReferenceEquals(_iedBrowserViewportDevice, device) ||
            !ReferenceEquals(_iedBrowserViewportPage, _iedBrowserRenderedPage) ||
            _iedBrowserRenderedPage?.Kind is "RCB" or "GoCB" or "SGCB" ||
            _iedBrowserViewportIndex is not { } index)
            return null;

        var changes = device.LiveValues.ReadChanges(
            _iedBrowserLastLiveVersion, maxChanges: 4096);
        return index.TryFindDirtyRows(changes, newestVersion, out var dirty)
            ? dirty : null;
    }
}
