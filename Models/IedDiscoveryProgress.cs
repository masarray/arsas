namespace ArIED61850Tester.Models;

/// <summary>
/// Real milestone progress for one independent IEC 61850 IED discovery session.
/// Each device owns its own progress instance, so several IED cards can scan at once.
/// </summary>
public enum IedDiscoveryStage
{
    PreparingSession,
    OpeningTcp,
    AssociatingMms,
    DiscoveringDirectory,
    BuildingLiveModel,
    BrowsingSupplementalNames,
    MappingSignals,
    ProbingLogicalNodes,
    ProbingPrimaryEquipment,
    ResolvingOperationalReferences,
    EnrichingEngineeringUnits,
    AnalyzingReporting,
    ResolvingIdentity,
    PreparingWorkspace,
    Complete
}

public sealed record IedDiscoveryProgress(
    IedDiscoveryStage Stage,
    string Message,
    double Percent,
    int Step,
    int TotalSteps,
    int? Completed = null,
    int? Total = null)
{
    public double NormalizedPercent => Math.Clamp(Percent, 0d, 100d);

    public string StepText
    {
        get
        {
            if (Completed.HasValue && Total is > 0)
                return $"{Completed.Value:N0} / {Total.Value:N0}";

            return TotalSteps > 0
                ? $"Step {Math.Clamp(Step, 0, TotalSteps)} of {TotalSteps}"
                : string.Empty;
        }
    }
}
