using System.Windows;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Browser Control action is a consumer of existing Command Dock live
/// authority, NEVER a fresh speculative IED-wide lookup. The existing native
/// ControlCommandWindow remains responsible for inspecting the live command
/// descriptor and obtaining an explicit operator confirmation before sending.
/// </summary>
public partial class MainWindow
{
    private (SignalDefinition Signal, LiveIedDataObjectModel Object)?
        BrowserSelectedLiveControl()
    {
        var device = SelectedDevice;
        if (device is not { IsConnected: true, IsDemo: false, IsBusy: false } ||
            device.LiveDiscoveryModel is null || device.LiveValues.IsOffline)
            return null;
        // A CO ctlVal leaf is a control action on its exact DO; all status
        // leaves remain read-only. This does not send any request on selection.
        var leaf = IedBrowserDetailList?.SelectedItem as IedBrowserDetailRow;
        var coValue = leaf is { Kind: "Data", IsDataObject: false,
                                FunctionalConstraint: "CO" } &&
                      (leaf.ValueReference.EndsWith("$ctlVal", StringComparison.Ordinal) ||
                       leaf.ValueReference.EndsWith(".ctlVal", StringComparison.Ordinal)) &&
                      (leaf.ValueReference.Contains("$Oper$", StringComparison.Ordinal) ||
                       leaf.ValueReference.Contains("$SBOw$", StringComparison.Ordinal) ||
                       leaf.ValueReference.Contains(".Oper.", StringComparison.Ordinal) ||
                       leaf.ValueReference.Contains(".SBOw.", StringComparison.Ordinal));
        if (leaf is { Kind: "Data", IsDataObject: false } && !coValue)
            return null;
        var page = _iedBrowserNavigation.Current;
        var focused = page?.Payload switch
        {
            LiveIedDataObjectModel obj => obj.Reference,
            LiveIedLogicalNodeModel => _iedBrowserSelectedDoReference,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(focused)) return null;

        var slash = focused.IndexOf('/');
        var name = slash > 0 ? focused[(slash + 1)..] : "";
        var dot = name.IndexOf('.');
        if (slash < 1 || dot < 1) return null;

        // Exact owner lookup within one LD/LN, not a visible-grid scan.
        var source = device.LiveDiscoveryModel.LogicalDevices
            .FirstOrDefault(ld =>
                string.Equals(ld.MmsDomain, focused[..slash], StringComparison.Ordinal))?
            .LogicalNodes.FirstOrDefault(ln =>
                string.Equals(ln.Name, name[..dot], StringComparison.Ordinal))?
            .DataObjects.FirstOrDefault(obj =>
                string.Equals(obj.Reference, focused, StringComparison.Ordinal));
        if (source is null) return null;
        if (coValue && !source.Attributes.Any(a =>
                a.FunctionalConstraint == "CO" &&
                string.Equals(a.MmsReference, leaf!.ValueReference, StringComparison.Ordinal)))
            return null;

        // This collection is already filtered by the Command Dock live
        // operational verification. All StatusOnly/unknown stay outside.
        var candidates = device.OperableCommandSignals.Where(signal =>
            signal.IsValidControlObject &&
            signal.HasControlServiceEvidence &&
            signal.ControlSemanticVerified &&
            signal.ControlModelResolved &&
            string.Equals(signal.ObjectReference, focused, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates.Length != 1) return null;
        var command = candidates[0];
        var inspected = new Iec61850ControlCapabilities
        {
            ObjectReference = command.ObjectReference,
            ControlModelReference = command.ControlModelReference,
            StatusReference = command.ControlStatusReference,
            ControlModel = command.ControlModelKind,
            ControlCdc = command.VerifiedLiveControlCdc,
            CtlValSignature = command.ControlValueType,
            EngineControlServiceAvailable = command.HasControlServiceEvidence,
            IsOperationallyReady = command.ControlSupportsOperate
        };
        var allow = IedBrowserControlEligibility.Check(
            focusIsExactDo: true, focusedReference: focused,
            onlineDo: source, liveControl: inspected,
            currentAssociationVerified: !device.LiveValues.IsOffline,
            operatorAuthorized: true);
        return allow.CanControl ? (command, source) : null;
    }

    private SignalDefinition? BrowserSelectedControlModelSignal()
    {
        var device = SelectedDevice;
        if (device is not { IsConnected: true, IsDemo: false, IsBusy: false } ||
            device.LiveDiscoveryModel is null || device.LiveValues.IsOffline ||
            _iedBrowserReadOperation is not null)
            return null;

        if (IedBrowserDetailList?.SelectedItem is not IedBrowserDetailRow
                { Kind: "Data", IsDataObject: false,
                  FunctionalConstraint: "CF" } row)
            return null;
        if (!row.ValueReference.EndsWith("$ctlModel", StringComparison.Ordinal) &&
            !row.ValueReference.EndsWith(".ctlModel", StringComparison.Ordinal))
            return null;

        var page = _iedBrowserNavigation.Current;
        var focused = page?.Payload switch
        {
            LiveIedDataObjectModel obj => obj.Reference,
            LiveIedLogicalNodeModel => _iedBrowserSelectedDoReference,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(focused)) return null;
        var slash = focused.IndexOf('/');
        var dot = slash < 0 ? -1 : focused.IndexOf('.', slash + 1);
        if (slash < 1 || dot <= slash + 1) return null;

        var source = device.LiveDiscoveryModel.LogicalDevices
            .FirstOrDefault(ld => string.Equals(ld.MmsDomain, focused[..slash],
                StringComparison.Ordinal))?
            .LogicalNodes.FirstOrDefault(ln => string.Equals(ln.Name,
                focused[(slash + 1)..dot], StringComparison.Ordinal))?
            .DataObjects.FirstOrDefault(obj =>
                string.Equals(obj.Reference, focused, StringComparison.Ordinal));
        if (source is null || !source.Attributes.Any(attr =>
                attr.FunctionalConstraint == "CF" &&
                string.Equals(attr.MmsReference, row.ValueReference, StringComparison.Ordinal)))
            return null;

        return device.OperableCommandSignals.Concat(device.CommandSignals)
            .Where(signal => signal.IsValidControlObject &&
                string.Equals(signal.ObjectReference, focused, StringComparison.Ordinal))
            .Distinct().FirstOrDefault();
    }

    private void RefreshIedBrowserControlButton()
    {
        if (IedBrowserControlButton is null) return;
        var current = BrowserSelectedLiveControl();
        IedBrowserControlButton.IsEnabled = current is not null;
        IedBrowserControlButton.ToolTip = current is null
            ? "Control disabled: focus a DO with existing live-proven ctlModel/CDC/CO. StatusOnly is never operable."
            : "Inspect exact native IEC Control model and confirm the command explicitly; no automatic Operate.";
    }

    private void IedBrowserControl_Click(object sender, RoutedEventArgs e)
    {
        var control = BrowserSelectedLiveControl();
        if (control is null || SelectedDevice is not { } device)
        {
            RefreshIedBrowserControlButton();
            return;
        }
        // Reuse native Direct/SBO normal/enhanced safety pipeline. Its
        // Window_Loaded explicitly re-inspects current wire authority before
        // enabling Send Command; selection alone never issues any command.
        var dialog = new ControlCommandWindow(_runtime, device, control.Value.Signal)
        {
            Owner = this
        };
        dialog.ShowDialog();
        RefreshIedBrowserControlButton();
    }
}
