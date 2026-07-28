using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvSnapshotSafetyTests
{
    [Fact]
    public void CompleteContinuousWindow_IsCleanProof()
    {
        var result = CreateResult(capturedSamples: 160, targetSamples: 160);

        Assert.True(result.IsComplete);
        Assert.False(SmvSnapshotSafetyAssessment.HasCounterAnomaly(result));
        Assert.True(SmvSnapshotSafetyAssessment.IsCleanProof(result));
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void AnyCounterDiscontinuity_BlocksCleanProof(
        int gaps,
        int duplicates,
        int outOfOrder,
        int restarts)
    {
        var result = CreateResult(
            capturedSamples: 160,
            targetSamples: 160,
            gaps: gaps,
            duplicates: duplicates,
            outOfOrder: outOfOrder,
            restarts: restarts);

        Assert.True(SmvSnapshotSafetyAssessment.HasCounterAnomaly(result));
        Assert.False(SmvSnapshotSafetyAssessment.IsCleanProof(result));
    }

    [Fact]
    public void Restart_IsNamedInContinuityEvidence()
    {
        var result = CreateResult(capturedSamples: 160, targetSamples: 160, restarts: 1);

        var evidence = SmvSnapshotSafetyAssessment.BuildContinuityEvidence(result);

        Assert.Contains("restart 1", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteWindow_CannotPass()
    {
        var result = CreateResult(capturedSamples: 159, targetSamples: 160);

        Assert.False(result.IsComplete);
        Assert.False(SmvSnapshotSafetyAssessment.IsCleanProof(result));
    }

    [Fact]
    public void SelectionIdentity_NormalizesEquivalentEngineeringText()
    {
        var identity = SmvSnapshotSelectionIdentity.Create(
            "IED1LD0/LLN0$MSVCB01",
            "MU01",
            "IED1LD0/LLN0$Dataset01",
            "0x4000",
            "01-0C-CD-04-00-01");

        Assert.True(identity.Matches(
            "ied1ld0/lln0.msvcb01",
            "mu01",
            "ied1ld0/lln0.dataset01",
            "4000",
            "01:0c:cd:04:00:01"));
    }

    [Fact]
    public void SelectionIdentity_RejectsDifferentStream()
    {
        var identity = SmvSnapshotSelectionIdentity.Create(
            "IED1LD0/LLN0$MSVCB01",
            "MU01",
            "IED1LD0/LLN0$Dataset01",
            "4000",
            "01-0C-CD-04-00-01");

        Assert.False(identity.Matches(
            "IED1LD0/LLN0$MSVCB02",
            "MU02",
            "IED1LD0/LLN0$Dataset02",
            "4001",
            "01-0C-CD-04-00-02"));
    }

    private static SmvSnapshotResult CreateResult(
        int capturedSamples,
        int targetSamples,
        int gaps = 0,
        int duplicates = 0,
        int outOfOrder = 0,
        int restarts = 0)
        => new()
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow.AddMilliseconds(40),
            CapturedSamples = capturedSamples,
            TargetSamples = targetSamples,
            GapTransitions = gaps,
            DuplicateTransitions = duplicates,
            OutOfOrderTransitions = outOfOrder,
            RestartTransitions = restarts
        };
}
