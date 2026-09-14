namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    // Existing dispatcher callbacks use QueueP1D5CursorMeasurements as a parameterless Action.
    // Keep that contract while the hot path can additionally target only C1 or C2.
    private void QueueP1D5CursorMeasurements()
        => QueueP1D5CursorMeasurements(P1D5MeasurementTargets.Both);
}
