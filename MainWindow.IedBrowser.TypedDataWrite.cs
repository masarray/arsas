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

    // A DO focus has a bounded local list of live attributes, never an IED-wide scan.
    // The dialog checks the exact same current live model and association again.
    private LiveIedDataObjectModel? BrowserWritableSelectedDo()
    {
        if (SelectedDevice is not { IsConnected: true, IsDemo: false, IsBusy: false,
                                   IsMonitoring: false, IsBrowserReading: false } device ||
            device.LiveValues.IsOffline || device.LiveDiscoveryModel is null ||
            _iedBrowserReadOperation is not null)
            return null;
        // A focused status-only DA cannot borrow its parent DO's writable siblings.
        if (IedBrowserDetailList?.SelectedItem is IedBrowserDetailRow
            { Kind: "Data", IsDataObject: false })
            return null;

        var page = _iedBrowserNavigation.Current;
        var focused = page?.Payload switch
        {
            LiveIedDataObjectModel obj => obj.Reference,
            LiveIedLogicalNodeModel when !string.IsNullOrWhiteSpace(_iedBrowserSelectedDoReference)
                => _iedBrowserSelectedDoReference,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(focused)) return null;
        var slash = focused.IndexOf('/');
        var dot = slash < 0 ? -1 : focused.IndexOf('.', slash + 1);
        if (slash < 1 || dot <= slash + 1) return null;

        var owner = device.LiveDiscoveryModel.LogicalDevices
            .FirstOrDefault(ld => string.Equals(ld.MmsDomain, focused[..slash], StringComparison.Ordinal))?
            .LogicalNodes.FirstOrDefault(ln => string.Equals(ln.Name,
                focused[(slash + 1)..dot], StringComparison.Ordinal))?
            .DataObjects.FirstOrDefault(obj => string.Equals(obj.Reference, focused, StringComparison.Ordinal));
        if (owner is null || owner.Attributes.Count is 0 or > 256) return null;

        return owner.Attributes.Any(a =>
            a.FunctionalConstraint is "CF" or "SP" or "DC" &&
            !string.IsNullOrWhiteSpace(a.MmsReference) &&
            !a.MmsReference.Contains("$Oper", StringComparison.Ordinal) &&
            !a.MmsReference.Contains("$SBO", StringComparison.Ordinal) &&
            !a.MmsReference.EndsWith("$ctlModel", StringComparison.Ordinal))
            ? owner : null;
    }

    private void RefreshIedBrowserWriteButton()
    {
        if (IedBrowserWriteButton is null) return;
        var ctlModel = BrowserSelectedControlModelSignal();
        var selected = ctlModel is null ? BrowserWritableSelectedAttribute() : null;
        var selectedDo = ctlModel is null && selected is null ? BrowserWritableSelectedDo() : null;
        IedBrowserWriteButton.IsEnabled =
            ctlModel is not null || selected is not null || selectedDo is not null;
        IedBrowserWriteButton.Content = ctlModel is not null ? "Write ctlModel…" :
            selectedDo is not null ? "Write DAs…" : "Write DA…";
        IedBrowserWriteButton.ToolTip = ctlModel is not null
            ? "Read live ctlModel, choose IEC enum 0–4, confirm a guarded CF Write and verify readback."
            : selected is not null
                ? "Exact DA: inspect live GVA, review old/new and confirm one MMS Write."
                : selectedDo is not null
                    ? "Exact DO: select changed attributes, inspect and confirm sequential non-atomic Writes."
                    : "Select an exact CF/SP/DC DA, ctlModel CF leaf or owning DO. CO members use Control.";
    }

    private void IedBrowserDetailList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, IedBrowserDetailList))
        {
            RefreshIedBrowserWriteButton();
            RefreshIedBrowserControlButton();
        }
    }

    private void IedBrowserWrite_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        var modelSignal = BrowserSelectedControlModelSignal();
        var attribute = modelSignal is null ? BrowserWritableSelectedAttribute() : null;
        var owner = modelSignal is null && attribute is null ? BrowserWritableSelectedDo() : null;
        if (device is null || (modelSignal is null && attribute is null && owner is null))
        {
            IedBrowserSubtitle.Text =
                "Write requires a current online CF/SP/DC attribute or exact owning DO.";
            RefreshIedBrowserWriteButton();
            return;
        }

        if (modelSignal is not null)
        {
            // Route ctlModel through the existing dedicated engine service,
            // not generic CF Write and NEVER through CO Operate.
            var dialog = new ControlModelWriteWindow(_runtime, device, modelSignal)
            {
                Owner = this
            };
            var changed = dialog.ShowDialog() == true;
            if (changed && device.IsConnected)
                _ = RefreshControlValuesAsync(device, force: true);
        }
        else if (attribute is not null)
        {
            var dialog = new BrowserDataWriteWindow(_runtime, device, attribute)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        else if (owner is not null && device.LiveDiscoveryModel is { } model)
        {
            var dialog = new BrowserBatchWriteWindow(_runtime, device, model, owner)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        RefreshIedBrowserWriteButton();
        // A successful MMS Write is NOT process feedback. An explicit Read or
        // subsequent verified report remains the only live value authority.
    }
}
