using ArIED61850Tester;
using System.ComponentModel;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatImmediateEvidenceRegressionTests
{
    [Fact]
    public void TrueEdge_PublishesOnTimestampBeforeFalseEdgeArrives()
    {
        var evaluator = new IoTestTransitionEvaluator();
        var point = CreatePoint();
        var onTimestampNotificationRaised = false;

        point.Runtime.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IoTestPointRuntime.OnRelayTimestampText))
                onTimestampNotificationRaised = true;
        };

        evaluator.StartAttempt(point, Observation(false, 1));
        var on = evaluator.Observe(point, Observation(true, 2));

        Assert.Equal(IoTestPointState.OnCaptured, on.State);
        Assert.NotNull(point.Runtime.OnEvidence);
        Assert.Null(point.Runtime.OffEvidence);
        Assert.Equal(
            Iec61850TimestampPresentation.FormatMilliseconds(
                point.Runtime.OnEvidence!.IedTimestamp,
                "yyyy-MM-dd HH:mm:ss.fff"),
            point.Runtime.OnRelayTimestampText);
        Assert.NotEqual("—", point.Runtime.OnRelayTimestampText);
        Assert.True(onTimestampNotificationRaised);
    }

    private static IoTestPointPlan CreatePoint()
        => new()
        {
            TestPointId = "TRUE-EVIDENCE-REGRESSION",
            IedName = "SIPROTEC",
            IpAddress = "192.168.1.10",
            SignalName = "Protection pickup",
            ObjectReference = "SIPROTEC/PROT.Op.general",
            FunctionalConstraint = "ST",
            ExpectedOnText = "true",
            ExpectedOffText = "false",
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var captured = new DateTimeOffset(2026, 8, 13, 4, 55, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            state,
            state ? "true" : "false",
            captured,
            captured.AddMilliseconds(-4),
            "Good",
            "BRCB",
            sequence,
            1);
    }
}
