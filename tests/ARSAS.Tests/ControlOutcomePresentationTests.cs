using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class ControlOutcomePresentationTests
{
    private static Iec61850ControlCommandResult Outcome(
        string completion = "Accepted", bool service = false, bool success = false,
        bool feedback = false, bool termination = false, bool positive = false,
        IReadOnlyList<Iec61850ControlWireEvidence>? wire = null) => new()
        {
            CompletionState = completion, ServiceAccepted = service,
            IsSuccess = success, FeedbackConfirmed = feedback,
            CommandTerminationReceived = termination, PositiveTermination = positive,
            WireSteps = wire ?? Array.Empty<Iec61850ControlWireEvidence>()
        };

    [Fact]
    public void ServiceAccepted_ButNoFeedback_NeverClaimsIedRejectionOrPhysicalSuccess()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome(completion: "PositiveTermination", service: true, success: false,
                feedback: false, termination: true, positive: true), false);
        Assert.Equal("Feedback not verified", view.Stage);
        Assert.Equal("Warning", view.Tone);
        Assert.Contains("independent process feedback", view.Summary);
    }

    [Fact]
    public void VerifiedFeedback_IsTheOnlyPhysicalSuccessDisplay()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome(service: true, success: true, feedback: true), false);
        Assert.Equal("Feedback confirmed", view.Stage);
        Assert.Equal("Success", view.Tone);
    }

    [Theory]
    [InlineData("TimedOut")]
    [InlineData("AssociationLost")]
    [InlineData("Unknown")]
    [InlineData("Cancelled")]
    public void AmbiguousWireOutcome_IsNotMisreportedAsIedRejection(string completion)
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome(completion, service: false), false);
        Assert.Equal("Outcome uncertain", view.Stage);
        Assert.Equal("Warning", view.Tone);
    }

    [Fact]
    public void ExplicitNegativeTermination_IsDefinitive()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome("NegativeTermination", service: true,
                termination: true, positive: false), false);
        Assert.Equal("Negative termination", view.Stage);
        Assert.Equal("Error", view.Tone);
    }

    [Fact]
    public void RejectedSelect_DoesNotClaimOperate()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome("Rejected", wire: new[] { new Iec61850ControlWireEvidence
                { Action = "SelectWithValue", RequestAccepted = false } }), false);
        Assert.Equal("IED rejected", view.Stage);
    }

    [Fact]
    public void TestAccepted_IsNotPhysicalFeedback()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome("Accepted", service: true, success: true), true);
        Assert.Equal("Test response accepted", view.Stage);
        Assert.Equal("Warning", view.Tone);
    }

    [Fact]
    public void WireTimeline_UsesOnlyActualWireStepsAndTerminatesFromEvidence()
    {
        var result = Outcome("PositiveTermination", service: true,
            termination: true, positive: true,
            wire: new[]
            {
                new Iec61850ControlWireEvidence
                    { Action = "SelectWithValue", RequestAccepted = true },
                new Iec61850ControlWireEvidence
                    { Action = "Operate", RequestAccepted = true }
            });
        var text = Iec61850ControlOutcomePresentation.WireTimeline(result);
        Assert.Equal("SBOw ✓  →  Operate ✓  →  Termination ✓", text);
        Assert.DoesNotContain("feedback", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WireTimeline_NoReportedSteps_MustNotInventSelectOrOperate()
    {
        Assert.Equal("", Iec61850ControlOutcomePresentation.WireTimeline(Outcome()));
        var negative = Outcome("Rejected", wire: new[]
        {
            new Iec61850ControlWireEvidence
                { Action = "Select", RequestAccepted = false }
        });
        Assert.Equal("Select rejected", Iec61850ControlOutcomePresentation.WireTimeline(negative));
    }

    [Fact]
    public void AlreadyInPosition_IsVerifiedLiveNoOp_NotACommandOrWireSuccess()
    {
        var result = new Iec61850ControlCommandResult
        {
            IsSuccess = true,
            ServiceAccepted = false,
            FeedbackConfirmed = true,
            CompletionState = "NotSent",
            Stage = "Already at requested state"
        };
        var view = Iec61850ControlOutcomePresentation.From(result, false);
        Assert.Equal("Already in position", view.Stage);
        Assert.Equal("Ready", view.Tone);
        Assert.Contains("No Select or Operate was sent", view.Summary);
        Assert.Empty(result.WireSteps);
    }

    [Fact]
    public void EncodedControlButNoProvenResponse_IsNotRejection()
    {
        var view = Iec61850ControlOutcomePresentation.From(
            Outcome(completion: "WireStateUnknown", service: false), false);
        Assert.Equal("Outcome uncertain", view.Stage);
        Assert.Equal("Warning", view.Tone);
    }

    [Fact]
    public void NoDispatchAndNoKnownOutcome_AreDifferent()
    {
        Assert.Equal("Not sent",
            Iec61850ControlOutcomePresentation.From(Outcome("NotSent"), false).Stage);
        Assert.Equal("Outcome unresolved",
            Iec61850ControlOutcomePresentation.From(Outcome("Unexpected"), false).Stage);
    }
}
