using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Compatibility bridge for the legacy RCB-filter dialog. Physical bench testing proved the
/// legacy dialog can still be reached on some input paths, so its selected rows are now sent
/// through the same generic multi-RCB export engine as the production multi-select window.
/// No ARIEC61850 engine code is changed.
/// </summary>
public partial class MainWindow
{
    internal async Task<RcbExportCompletion> ExportP1LegacyMultiRcbAsync(
        IReadOnlyList<RcbExportRow> selectedRows,
        SclSchemaProfile schema,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (selectedRows == null || selectedRows.Count == 0)
            throw new InvalidOperationException("Select at least one RCB before export.");

        var device = SelectedDevice
            ?? throw new InvalidOperationException("The Engineering IED owning this RCB filter is no longer selected.");

        MmsRcbAvailabilityResult? availability = null;
        if (device.IsConnected)
        {
            availability = await _rcbAvailabilityProbe
                .CheckAsync(device, cancellationToken)
                .ConfigureAwait(true);
        }

        return await ExportGenericMultiRcbAsync(
                device,
                selectedRows,
                schema,
                outputPath,
                availability,
                cancellationToken)
            .ConfigureAwait(true);
    }
}
