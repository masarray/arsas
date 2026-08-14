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
        Iec61850DataAttributeSemanticRole role)
        => new()
        {
            DesignReference = designReference,
            PrimaryValueReference = primaryValueReference,
            SemanticRole = role,
            FunctionalConstraint = "MX",
            IsStaticDataSetMandatory = true
        };
}
