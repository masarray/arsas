using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class IedBrowserTypedDataWriteGuardTests
{
    private static MmsObjectReference Cf(string name = "sboTimeout")
        => MmsObjectReference.Parse("ZIVAVRLD1/ATCC1.TapChg." + name, "CF");

    private static MmsVariableAccessAttributesResult Gva(
        MmsObjectReference reference, string type, int? bits = null,
        MmsTypeSizeConstraintKind sizing = MmsTypeSizeConstraintKind.None)
        => new()
        {
            IsSuccess = true,
            Reference = reference,
            TypeSpecification = new MmsTypeSpecificationNode
            {
                MmsType = type,
                Size = bits,
                SizeConstraintKind = sizing
            }
        };

    private static string Finger(string type, int? size = null,
        MmsTypeSizeConstraintKind constraint = MmsTypeSizeConstraintKind.None)
        => IedBrowserTypedWriteAdmission.Fingerprint(new MmsTypeSpecificationNode
        {
            MmsType = type, Size = size, SizeConstraintKind = constraint
        });

    [Fact]
    public void SingleCfSigned32_AcceptsExactLiveTypeAndOriginalFc()
    {
        var reference = Cf();
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            reference, Gva(reference, "integer", 32), Finger("integer", 32), MmsDataValue.Integer(3)));
    }

    [Fact]
    public void CtlModel_UsesExistingDedicatedConfigurationService()
    {
        var r = Cf("ctlModel");
        Assert.Contains("ConfigureControlModel", IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(r, "integer", 32), Finger("integer", 32), MmsDataValue.Integer(1))!);
    }

    [Fact]
    public void ControlService_MustNeverUseGenericWriter_EvenWithForgedCf()
    {
        var co = MmsObjectReference.Parse("LD/CSWI1.Pos.Oper.ctlVal", "CO");
        Assert.Contains("Control-service", IedBrowserTypedWriteAdmission.RejectReason(
            co, Gva(co, "boolean"), Finger("boolean"), MmsDataValue.Boolean(true))!);
        var forged = MmsObjectReference.Parse("LD/CSWI1.Pos.Oper.ctlVal", "CF");
        Assert.Contains("Control-service", IedBrowserTypedWriteAdmission.RejectReason(
            forged, Gva(forged, "boolean"), Finger("boolean"), MmsDataValue.Boolean(true))!);
    }

    [Theory]
    [InlineData("ST")]
    [InlineData("MX")]
    [InlineData("SG")]
    [InlineData("SE")]
    [InlineData("SV")]
    [InlineData("RP")]
    public void UnapprovedFunctionalConstraints_FailClosed(string fc)
    {
        var r = MmsObjectReference.Parse("LD/XCBR1.Pos.stVal", fc);
        Assert.Contains("no approved", IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(r, "boolean"), Finger("boolean"), MmsDataValue.Boolean(true))!);
    }

    [Fact]
    public void FcInWrongMmsSegment_DoesNotGrantAuthority()
    {
        var reference = new MmsObjectReference("LD", "CSWI1$ST$Pos$CF$ctlModel", "CF");
        Assert.Contains("exact requested FC", IedBrowserTypedWriteAdmission.RejectReason(
            reference, Gva(reference, "integer", 32), Finger("integer", 32), MmsDataValue.Integer(3))!);
    }

    [Fact]
    public void StaleGvaSignatureOrReference_RejectsBeforeWrite()
    {
        var r = Cf();
        Assert.Contains("mismatched", IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(r, "integer", 32), Finger("boolean"), MmsDataValue.Integer(2))!);
        Assert.Contains("mismatched", IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(Cf("differentAttribute"), "integer", 32), Finger("integer", 32), MmsDataValue.Integer(2))!);
    }

    [Fact]
    public void IntegerAndUnsignedWidths_DoNotSilentlyOverflowOrFlipSign()
    {
        var r = Cf();
        var signed = Gva(r, "integer", 8);
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            r, signed, Finger("integer", 8), MmsDataValue.Integer(-128)));
        Assert.Contains("outside", IedBrowserTypedWriteAdmission.RejectReason(
            r, signed, Finger("integer", 8), MmsDataValue.Integer(128))!);
        var unsigned = Gva(r, "unsigned", 16);
        Assert.Contains("unsigned", IedBrowserTypedWriteAdmission.RejectReason(
            r, unsigned, Finger("unsigned", 16), MmsDataValue.Integer(-1))!);
        Assert.Contains("outside", IedBrowserTypedWriteAdmission.RejectReason(
            r, unsigned, Finger("unsigned", 16), MmsDataValue.Unsigned(65536))!);
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            r, unsigned, Finger("unsigned", 16), MmsDataValue.Unsigned(65535)));
    }

    [Fact]
    public void Float64_IsBlockedUntilLosslessEngineCodecExists()
    {
        var r = Cf("setMag");
        Assert.Contains("FLOAT32", IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(r, "floating-point", 64), Finger("floating-point", 64),
            MmsDataValue.FloatingPoint(1.1d))!);
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            r, Gva(r, "floating-point", 32), Finger("floating-point", 32),
            MmsDataValue.FloatingPoint(1.25f)));
    }

    [Fact]
    public void Strings_AreValidatedAgainstLiveWireLengthAndLosslessness()
    {
        var r = Cf("d");
        var type = Gva(r, "visible-string", 5, MmsTypeSizeConstraintKind.Maximum);
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            r, type, Finger("visible-string", 5, MmsTypeSizeConstraintKind.Maximum), MmsDataValue.VisibleString("hello")));
        Assert.Contains("length", IedBrowserTypedWriteAdmission.RejectReason(
            r, type, Finger("visible-string", 5, MmsTypeSizeConstraintKind.Maximum), MmsDataValue.VisibleString("longer"))!);
        Assert.Contains("Non-ASCII", IedBrowserTypedWriteAdmission.RejectReason(
            r, type, Finger("visible-string", 5, MmsTypeSizeConstraintKind.Maximum), MmsDataValue.VisibleString("é"))!);
    }

    [Fact]
    public void BitString_RequiresExactBitsAndZeroUnusedPadding()
    {
        var r = Cf("bits");
        var type = Gva(r, "bit-string", 2, MmsTypeSizeConstraintKind.Fixed);
        Assert.Null(IedBrowserTypedWriteAdmission.RejectReason(
            r, type, Finger("bit-string", 2, MmsTypeSizeConstraintKind.Fixed), MmsDataValue.BitString(6, new byte[] { 0b_1000_0000 })));
        Assert.Contains("padding", IedBrowserTypedWriteAdmission.RejectReason(
            r, type, Finger("bit-string", 2, MmsTypeSizeConstraintKind.Fixed), MmsDataValue.BitString(6, new byte[] { 0b_1000_0001 }))!);
    }

    private static IedBrowserTypedWriteStep Step(string attribute) => new(
        Cf(attribute), Finger("integer", 32), MmsDataValue.Integer(1), MmsDataValue.Integer(2));

    [Fact]
    public void Plan_RejectsDuplicateAndCrossDo_WithoutDispatch()
    {
        Assert.Throws<ArgumentException>(() => IedBrowserTypedWriteBatch.Freeze(
            new[] { Step("a"), Step("a") }));
        Assert.Throws<ArgumentException>(() => IedBrowserTypedWriteBatch.Freeze(
            new[] { Step("a"), new IedBrowserTypedWriteStep(
                MmsObjectReference.Parse("LD/ATCC1.Other.b", "CF"),
                Finger("integer", 32), MmsDataValue.Integer(1), MmsDataValue.Integer(2)) }));
    }

    [Fact]
    public async Task Batch_OneConfirmedOneRejectedThirdNotAttempted()
    {
        var attempts = 0;
        var plan = IedBrowserTypedWriteBatch.Freeze(
            new[] { Step("a"), Step("b"), Step("c") });
        var result = await IedBrowserTypedWriteBatch.ExecuteAsync(
            plan,
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new IedBrowserNativeWriteResult(
                    attempts == 1 ? IedBrowserNativeWriteState.AcceptedVerified :
                        IedBrowserNativeWriteState.Rejected, attempts == 1 ? "readback matches" : "access-denied"));
            }, () => true, CancellationToken.None);
        Assert.Equal(2, attempts);
        Assert.Equal(1, result.Verified);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.Unattempted);
        Assert.Null(result.Rows[2].State);
    }

    [Fact]
    public async Task UnknownOutcome_NeverRetriesAndStopsRemainingRequests()
    {
        var attempts = 0;
        var plan = IedBrowserTypedWriteBatch.Freeze(new[] { Step("a"), Step("b") });
        var result = await IedBrowserTypedWriteBatch.ExecuteAsync(
            plan,
            (_, _) =>
            {
                attempts++;
                throw new IOException("association lost after dispatch");
            }, () => true, CancellationToken.None);
        Assert.Equal(1, attempts);
        Assert.Equal(1, result.Unknown);
        Assert.Equal(1, result.Unattempted);
    }

    [Fact]
    public async Task StaleAssociation_FencesEntireBatchBeforeWire()
    {
        var attempts = 0;
        var result = await IedBrowserTypedWriteBatch.ExecuteAsync(
            IedBrowserTypedWriteBatch.Freeze(new[] { Step("a"), Step("b") }),
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.AcceptedVerified, "ok"));
            }, () => false, CancellationToken.None);
        Assert.Equal(0, attempts);
        Assert.Equal(2, result.Unattempted);
    }
}
