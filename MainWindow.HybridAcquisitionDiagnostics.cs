using System.Collections.ObjectModel;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// P5 per-signal acquisition evidence remains available to diagnostics/export code,
    /// but P6 intentionally stops rendering it as a continuously refreshed acquisition table.
    /// The 1.5 s clear/repopulate cycle made the Diagnostics workspace flicker between
    /// pending/empty/failure states during report setup and reconnect, obscuring the
    /// communication journal exactly when an engineer needs it most.
    /// </summary>
    public ObservableCollection<HybridSignalAcquisitionTelemetry> HybridAcquisitionTelemetry { get; } = new();
}
