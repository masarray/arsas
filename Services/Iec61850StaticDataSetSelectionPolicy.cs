using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Dataset-centric selection boundary used by the SCL Static DataSet workflow.
/// DataSet membership is authoritative; ordinary browsed IED signals and controls do
/// not leak into Live Signal Values merely because MMS can read them.
/// </summary>
public static class Iec61850StaticDataSetSelectionPolicy
{
    public static bool IsEligible(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return !signal.IsControlSignal &&
               signal.CanPublishToRuntime &&
               !string.IsNullOrWhiteSpace(signal.DataSetReference);
    }
}
