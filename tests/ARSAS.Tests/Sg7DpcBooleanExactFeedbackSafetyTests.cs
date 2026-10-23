using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Offline pinned-ARIEC typed descriptor contract only. Never sends SBO,
/// Operate, Config Write or any network command.
/// </summary>
public sealed class Sg7DpcBooleanExactFeedbackSafetyTests
{
    private static Iec61850ControlObjectDescriptor Descriptor(
        string objectReference, string statusReference,
        string wireType = "boolean") => new()
    {
        ObjectReference = objectReference,
        StatusReference = statusReference,
        Cdc = "SPC", // Vendor wire inference may still be SPC for Boolean ctlVal
        ControlModel = Iec61850ControlModel.DirectNormal,
        CtlValSpecification = new MmsTypeSpecificationNode
        {
            Name = "ctlVal", MmsType = wireType,
            Size = wireType == "bit-string" ? 2 : null
        },
        OperSpecification = new MmsTypeSpecificationNode { MmsType = "structure" }
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FourCswiBooleanCtlVal_RequireExactSameObjectLiveStVal(int n)
    {
        var reference = $"SG7_BCUSystem/BkrCSWI{n}.Pos";
        var descriptor = Descriptor(reference, reference + ".stVal");
        var semantic = Iec61850ControlSemanticResolver.Resolve(
            descriptor, "DPC", reference + ".stVal");
        Assert.True(semantic.IsCommandSafe);
        Assert.Equal("DPC", semantic.Cdc);
        Assert.True(NativeIec61850Client.HasExactDpcBooleanFeedback(
            descriptor, semantic.Cdc));

        var unverified = new SignalDefinition
        {
            ObjectReference = reference, FunctionalConstraint = "CO",
            IsControlSignal = true, HasControlServiceEvidence = true,
            ControlCdc = "DPC"
        };
        Assert.False(unverified.ControlSupportsOperate);
    }

    [Fact]
    public void OtherCswiStatusMayPassSuffixCheckButMustFailConsumerIdentityFence()
    {
        const string own = "SG7_BCUSystem/BkrCSWI1.Pos";
        const string another = "SG7_BCUSystem/BkrCSWI2.Pos.stVal";
        var descriptor = Descriptor(own, another);
        var semantic = Iec61850ControlSemanticResolver.Resolve(
            descriptor, "DPC");
        // This intentionally proves the consumer adds a stronger exact object
        // check without weakening or assuming the engine's suffix-based logic.
        Assert.True(semantic.IsCommandSafe);
        Assert.False(NativeIec61850Client.HasExactDpcBooleanFeedback(
            descriptor, semantic.Cdc));
    }

    [Theory]
    [InlineData("SG7_BCUSystem/BkrCSWI1.Pos.q")]
    [InlineData("SG7_BCUSystem/BkrCSWI1.Pos.stVal.extra")]
    [InlineData("SG7_BCUSystem/BkrCSWI1.Pos.STVAL")]
    public void NearMissFeedbackPathsCannotClaimDpcBooleanAuthority(string feedback)
    {
        var descriptor = Descriptor("SG7_BCUSystem/BkrCSWI1.Pos", feedback);
        Assert.False(NativeIec61850Client.HasExactDpcBooleanFeedback(
            descriptor, "DPC"));
    }

    [Fact]
    public void NonBooleanCodecKeepsExistingEngineVerificationAuthority()
    {
        const string own = "SG7_BCUSystem/BkrCSWI1.Pos";
        var descriptor = Descriptor(own, own + ".stVal", "bit-string");
        Assert.True(NativeIec61850Client.HasExactDpcBooleanFeedback(
            descriptor, "DPC"));
    }
}
