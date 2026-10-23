using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Only the currently selected, exact model-declared DA may enter the
    /// explicit staged write dialog. Selection does NOT contact MMS.
    /// Bounded to the visible LN/DO; never enumerate all IED rows.
    /// </summary>
    private LiveIedDataAttributeModel? BrowserWritableSelectedAttribute()
    {
        if (SelectedDevice is not { IsConnected: true, IsDemo: false, IsBusy: false,
                                   IsMonitoring: false, IsBrowserReading: false } device ||
            device.LiveDiscoveryModel is null ||
            _iedBrowserReadOperation is not null ||
            IedBrowserDetailList?.SelectedItem is not IedBrowserDetailRow
            {
                Kind: "Data", IsDataObject: false
            } row ||
            string.IsNullOrWhiteSpace(row.ValueReference) ||
            row.FunctionalConstraint is not ("CF" or "SP" or "DC"))
            return null;

        var page = _iedBrowserNavigation.Current;
        LiveIedDataObjectModel? owner = page?.Payload switch
        {
            LiveIedDataObjectModel obj => obj,
            LiveIedLogicalNodeModel ln when
                !string.IsNullOrWhiteSpace(_iedBrowserSelectedDoReference) =>
                ln.DataObjects.FirstOrDefault(obj =>
                    string.Equals(obj.Reference, _iedBrowserSelectedDoReference,
                        StringComparison.Ordinal)),
            _ => null
        };
        if (owner is null) return null;

        var model = device.LiveDiscoveryModel;
        // A navigated page or reconnected model cannot lend authority to
        // stale row objects. Resolve exact owner in CURRENT live LD/LN.
        var slash = owner.Reference.IndexOf('/');
        var dotted = slash < 0 ? "" : owner.Reference[(slash + 1)..];
        var dot = dotted.IndexOf('.');
        if (slash < 1 || dot < 1) return null;
        var domain = owner.Reference[..slash];
        var lnName = dotted[..dot];
        var liveOwner = model.LogicalDevices
            .FirstOrDefault(ld => string.Equals(ld.MmsDomain, domain, StringComparison.Ordinal))?
            .LogicalNodes.FirstOrDefault(ln => string.Equals(ln.Name, lnName, StringComparison.Ordinal))?
            .DataObjects.FirstOrDefault(obj => string.Equals(obj.Reference, owner.Reference, StringComparison.Ordinal));
        if (liveOwner is null) return null;
        // Never treat a synthetic parent display/reference as a selected
        // leaf. The clicked row must expose the EXACT MMS DA identity from
        // live discovery, not a canonicalised proxy/engineering projection.
        var candidates = liveOwner.Attributes.Where(attr =>
                string.Equals(attr.FunctionalConstraint, row.FunctionalConstraint,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(attr.MmsReference, row.ValueReference,
                    StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates.Length != 1) return null; // duplicates are ambiguous.
        var exact = candidates[0];
        var reference = MmsObjectReference.Parse(
            exact.MmsReference, exact.FunctionalConstraint);
        var parts = reference.Item.Split('$');
        if (parts.Length < 4 ||
            !string.Equals(parts[1], reference.FunctionalConstraint,
                StringComparison.OrdinalIgnoreCase) ||
            parts.Skip(2).Any(x => x is "Oper" or "SBO" or "SBOw" or "Cancel" or "ctlModel"))
            return null;
        return exact;
    }

    private void RefreshIedBrowserWriteButton()
    {
        if (IedBrowserWriteButton is null) return;
        var selected = BrowserWritableSelectedAttribute();
        IedBrowserWriteButton.IsEnabled = selected is not null;
        IedBrowserWriteButton.ToolTip = selected is null
            ? "Select one online, exact CF/SP/DC data attribute. Status, control and ctlModel use separate services."
            : "Inspect live GVA type/readback, preview a diff and confirm one MMS Write. No automatic Operate.";
    }

    private void IedBrowserDetailList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, IedBrowserDetailList))
            RefreshIedBrowserWriteButton();
    }

    private void IedBrowserWrite_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        var attribute = BrowserWritableSelectedAttribute();
        if (device is null || attribute is null)
        {
            IedBrowserSubtitle.Text =
                "Write requires an exact, current, online CF/SP/DC Data Attribute selection.";
            RefreshIedBrowserWriteButton();
            return;
        }

        var dialog = new BrowserDataWriteWindow(_runtime, device, attribute)
        {
            Owner = this
        };
        dialog.ShowDialog();
        RefreshIedBrowserWriteButton();
        // A successful MMS Write is NOT process feedback. An explicit Read or
        // subsequent verified report remains the only live value authority.
    }
}
