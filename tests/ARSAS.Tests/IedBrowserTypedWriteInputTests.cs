using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserTypedWriteInputTests
{
    private static MmsTypeSpecificationNode Type(string family, int size = 32)
        => new() { MmsType = family, Size = size };

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    public void BooleanExactText_Accepted(string raw, bool expected)
    {
        Assert.True(IedBrowserTypedWriteInput.TryParse(
            Type("boolean"), raw, out var typed, out _));
        Assert.Equal(expected, typed!.Value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("open")]
    public void BooleanNumericAndAlias_NeverCoerced(string raw)
    {
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("boolean"), raw, out _, out _));
    }

    [Fact]
    public void SignedNegativeAndUnsignedNegative_AreDifferentWireTypes()
    {
        Assert.True(IedBrowserTypedWriteInput.TryParse(
            Type("integer"), "-2", out var signed, out _));
        Assert.Equal(MmsDataKind.Integer, signed!.Kind);
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("unsigned"), "-2", out _, out _));
    }

    [Fact]
    public void FloatInvariantDecimal_RejectsLocaleCommaAndNonFinite()
    {
        Assert.True(IedBrowserTypedWriteInput.TryParse(
            Type("floating-point", 32), "1.25", out var typed, out _));
        Assert.Equal(1.25f, typed!.Value);
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("floating-point", 32), "1,25", out _, out _));
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("floating-point", 32), "NaN", out _, out _));
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("floating-point", 64), "1.25", out _, out _));
    }

    [Fact]
    public void Utf8MustNotBypassLiveAsciiWireAdmission()
    {
        var wireType = new MmsTypeSpecificationNode
        {
            MmsType = "visible-string", Size = 32,
            SizeConstraintKind = MmsTypeSizeConstraintKind.Maximum
        };
        Assert.True(IedBrowserTypedWriteInput.TryParse(
            wireType, "é", out var typed, out _));
        var r = MmsObjectReference.Parse("LD/LLN0.NamPlt.d", "DC");
        var gva = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true, Reference = r, TypeSpecification = wireType
        };
        Assert.NotNull(IedBrowserTypedWriteAdmission.RejectReason(
            r, gva, IedBrowserTypedWriteAdmission.Fingerprint(wireType), typed!));
    }

    [Fact]
    public void UnsupportedOrMissingLiveType_DoesNotBuildProposedValue()
    {
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            null, "1", out var missing, out _));
        Assert.Null(missing);
        Assert.False(IedBrowserTypedWriteInput.TryParse(
            Type("utc-time"), "2026-10-23", out var utc, out _));
        Assert.Null(utc);
    }
}
