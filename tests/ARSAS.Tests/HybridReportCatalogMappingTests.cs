using AR.Iec61850.Discovery;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class HybridReportCatalogMappingTests
{
    [Fact]
    public void PrimaryValueReference_IsNotAmbiguousWithItsQualityAndTimestampDescriptors()
    {
        const string valueReference = "IEDLD/MMXU1.Hz.instMag.f";
        var primary = Descriptor(valueReference, valueReference, Iec61850DataAttributeSemanticRole.PrimaryValue);
        var quality = Descriptor("IEDLD/MMXU1.Hz.q", valueReference, Iec61850DataAttributeSemanticRole.Quality);
        var timestamp = Descriptor("IEDLD/MMXU1.Hz.t", valueReference, Iec61850DataAttributeSemanticRole.Timestamp);
        var catalog = new Iec61850SignalCatalogDocument { Signals = [primary, quality, timestamp] };

        var index = NativeIec61850Client.BuildLiteralCatalogIndex(catalog);

        Assert.True(NativeIec61850Client.TryResolveLiteralCatalogSignal(index, valueReference, out var resolved));
        Assert.Same(primary, resolved);
    }

    [Fact]
    public void ExactDataSetMemberReference_ResolvesItsPrimaryValueDescriptor()
    {
        const string memberReference = "IEDLD/XCBR1.Pos";
        const string valueReference = "IEDLD/XCBR1.Pos.stVal";
        var primary = Descriptor(
            valueReference,
            valueReference,
            Iec61850DataAttributeSemanticRole.PrimaryValue,
            memberReference,
            isPrimaryValueForMember: true);
        var quality = Descriptor(
            "IEDLD/XCBR1.Pos.q",
            valueReference,
            Iec61850DataAttributeSemanticRole.Quality,
            memberReference,
            isPrimaryValueForMember: false);
        var timestamp = Descriptor(
            "IEDLD/XCBR1.Pos.t",
            valueReference,
            Iec61850DataAttributeSemanticRole.Timestamp,
            memberReference,
            isPrimaryValueForMember: false);
        var catalog = new Iec61850SignalCatalogDocument { Signals = [primary, quality, timestamp] };

        var index = NativeIec61850Client.BuildLiteralCatalogIndex(catalog);

        Assert.True(NativeIec61850Client.TryResolveLiteralCatalogSignal(index, memberReference, out var resolved));
        Assert.Same(primary, resolved);
    }

    [Fact]
    public void DuplicatePrimaryDataSetMemberReferences_RemainAmbiguous()
    {
        const string memberReference = "IEDLD/GGIO1.Ind";
        var first = Descriptor(
            "IEDLD/GGIO1.Ind1.stVal",
            "IEDLD/GGIO1.Ind1.stVal",
            Iec61850DataAttributeSemanticRole.PrimaryValue,
            memberReference,
            isPrimaryValueForMember: true);
        var second = Descriptor(
            "IEDLD/GGIO1.Ind2.stVal",
            "IEDLD/GGIO1.Ind2.stVal",
            Iec61850DataAttributeSemanticRole.PrimaryValue,
            memberReference,
            isPrimaryValueForMember: true);
        var catalog = new Iec61850SignalCatalogDocument { Signals = [first, second] };

        var index = NativeIec61850Client.BuildLiteralCatalogIndex(catalog);

        Assert.False(NativeIec61850Client.TryResolveLiteralCatalogSignal(index, memberReference, out _));
    }

    [Fact]
    public void TrueDuplicateDirectReferences_RemainAmbiguous()
    {
        const string reference = "IEDLD/GGIO1.Ind1.stVal";
        var first = Descriptor(reference, reference, Iec61850DataAttributeSemanticRole.PrimaryValue);
        var second = Descriptor(reference, reference, Iec61850DataAttributeSemanticRole.PrimaryValue);
        var catalog = new Iec61850SignalCatalogDocument { Signals = [first, second] };

        var index = NativeIec61850Client.BuildLiteralCatalogIndex(catalog);

        Assert.False(NativeIec61850Client.TryResolveLiteralCatalogSignal(index, reference, out _));
    }

    private static Iec61850SignalDescriptor Descriptor(
        string designReference,
        string primaryValueReference,
        Iec61850DataAttributeSemanticRole role,
        string? dataSetMemberReference = null,
        bool isPrimaryValueForMember = false)
        => new()
        {
            DesignReference = designReference,
            PrimaryValueReference = primaryValueReference,
            SemanticRole = role,
            FunctionalConstraint = "MX",
            IsStaticDataSetMandatory = true,
            DataSetMemberships = string.IsNullOrWhiteSpace(dataSetMemberReference)
                ? Array.Empty<Iec61850SignalDataSetMembership>()
                :
                [
                    new Iec61850SignalDataSetMembership
                    {
                        DataSetReference = "IEDLD/LLN0.dsStatic",
                        MemberIndex = 0,
                        OriginalMemberReference = dataSetMemberReference,
                        CanonicalMemberReference = dataSetMemberReference,
                        FunctionalConstraint = "MX",
                        IsPrimaryValueForMember = isPrimaryValueForMember
                    }
                ]
        };
}
