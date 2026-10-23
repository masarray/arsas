using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Reasserts the shared Engineering process image after a FAT control transaction.
    /// Command-service return values are transaction evidence; they must never outrank the
    /// latest live status point that Engineering and FAT already share.
    /// </summary>
    internal void ReconcileIoFatCommandValueFromSharedProcessImage(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var device = _signalOwners.TryGetValue(signal, out var owner)
            ? owner
            : SelectedDevice;
        if (device == null || string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            return;

        var expected = NormalizeReference(signal.ControlStatusReference);
        var latest = device.Points
            .Where(point => NormalizeReference(point.IecReference)
                .Equals(expected, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(point => point.Sequence)
            .FirstOrDefault();

        if (latest == null || string.IsNullOrWhiteSpace(latest.Value) || latest.Value == "-")
            return;

        signal.ControlCurrentValue = latest.Value;
    }
}
