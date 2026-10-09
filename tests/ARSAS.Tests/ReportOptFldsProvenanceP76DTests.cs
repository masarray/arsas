using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>Wire OptFlds were already decoded in ARIEC; tests ensure Studio
/// preserves missing-vs-omitted evidence without inventing SOE proof.</summary>
public sealed class ReportOptFldsProvenanceP76DTests
{
    [Fact]
    public void SqNumOmittedOnWire_IsDifferentFromDecoderMissingField()
    {
        var state = new Iec61850ReportContinuityState();
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state,
            Frame("0000", sqOpt: false, entryOpt: false), false));
        Assert.Equal(1, state.OptFldsDecodedFrames);
        Assert.Equal(1, state.SqNumOmittedFrames);
        Assert.Equal(0, state.SqNumAdvertisedMissingFrames);
        Assert.Equal(1, state.EntryIdOmittedFrames);

        var snapshot = Snapshot(state);
        var diagnostic = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(diagnostic, snapshot);
        Assert.Contains("OptFldsDecoded=1", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains("SqNum requested=0, omitted=1, requestedButUndecoded=0",
            diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains("EntryID requested=0, omitted=1", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SqNumAdvertisedButMissing_IsFailVisibleNotSilent()
    {
        var state = new Iec61850ReportContinuityState();
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            Frame("4000", sqOpt: true, entryOpt: false), true);
        Assert.Contains(warnings, w => w.Contains("advertises SqNum", StringComparison.Ordinal));
        Assert.Equal(1, state.SqNumAdvertisedFrames);
        Assert.Equal(1, state.SqNumAdvertisedMissingFrames);
        Assert.Equal(1, state.AnomalyFindings);
        Assert.Null(state.LastSequenceNumber);
    }

    [Fact]
    public void UnknownProvenance_IsNotMisclassifiedAsOmitted()
    {
        var state = new Iec61850ReportContinuityState();
        var frame = new NativeReportFrameMetadata { SequenceNumber = 0 };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, frame, true));
        Assert.Equal(1, state.OptFldsUnknownFrames);
        Assert.Equal(0, state.OptFldsDecodedFrames);
        Assert.Equal(0, state.SqNumOmittedFrames);
        Assert.Equal(0, state.SqNumAdvertisedFrames);
    }

    [Fact]
    public void PhysicalZeroZeroSequence_RemainsUnverifiedEvenWithOptFldsKnown()
    {
        var state = new Iec61850ReportContinuityState();
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state,
            Frame("4000", sqOpt: true, entryOpt: false, seq: 0), true));
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            Frame("4000", sqOpt: true, entryOpt: false, seq: 0), true);
        Assert.Contains(warnings, w => w.Contains("duplicate/replay", StringComparison.Ordinal));
        Assert.Equal(2, state.SqNumAdvertisedFrames);
        Assert.Equal(1, state.AnomalyFindings);
        Assert.Equal(0, state.EntryIdAdvertisedFrames);
        Assert.Equal(2, state.EntryIdOmittedFrames);
    }

    [Fact]
    public void AdvertisedEntryIdWithEmptyValue_DoesNotClaimTransportLoss()
    {
        var state = new Iec61850ReportContinuityState();
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state,
            Frame("0100", sqOpt: false, entryOpt: true), true));
        Assert.Equal(1, state.EntryIdAdvertisedFrames);
        Assert.Equal(1, state.EntryIdAdvertisedEmptyFrames);
        Assert.Equal(0, state.AnomalyFindings);
        var diagnostic = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(diagnostic, Snapshot(state));
        Assert.Contains("emptyOrUnprojected=1", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ContradictoryOptFldsMetadata_IsWarnedWithoutDroppingValues()
    {
        var state = new Iec61850ReportContinuityState();
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            Frame("0000", sqOpt: false, entryOpt: false, seq: 9, entry: "AA"), true);
        Assert.Contains(warnings, w => w.Contains("omits SqNum", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("omits EntryID", StringComparison.Ordinal));
        Assert.Equal(9UL, state.LastSequenceNumber);
    }

    [Fact]
    public void MalformedOptFldsRawMask_IsUnknownAndNeverPrintedUnescaped()
    {
        var state = new Iec61850ReportContinuityState();
        var bad = new NativeReportFrameMetadata
        {
            OptFldsRawHex = "00\r\nStatic parity : SPOOF",
            OptFldsSequenceNumber = false,
            OptFldsEntryId = false
        };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, bad, true));
        Assert.Equal(1, state.OptFldsUnknownFrames);
        Assert.Equal(string.Empty, state.LastOptFldsHex);
        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb, Snapshot(state));
        Assert.DoesNotContain("SPOOF", sb.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_UsesAuthoritativeEngineDecodedOptionalFields_NotLocalMmsParsing()
    {
        var adapter = Read("Services/NativeIec61850Client.cs");
        Assert.Contains("var optFlds = header.OptionalFields;", adapter, StringComparison.Ordinal);
        Assert.Contains("optFlds.HasSequenceNumber", adapter, StringComparison.Ordinal);
        Assert.Contains("optFlds.HasEntryId", adapter, StringComparison.Ordinal);
        Assert.Contains("optFlds.RawHex", adapter, StringComparison.Ordinal);
        var inspector = Read("Services/Iec61850ReportContinuityInspector.cs");
        Assert.DoesNotContain("DecodeMms", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadReportAttributeAsync", inspector, StringComparison.Ordinal);
    }

    private static NativeReportFrameMetadata Frame(string hex, bool sqOpt, bool entryOpt,
        ulong? seq = null, string entry = "") => new()
    {
        OptFldsRawHex = hex,
        OptFldsSequenceNumber = sqOpt,
        OptFldsEntryId = entryOpt,
        SequenceNumber = seq,
        EntryIdHex = entry
    };

    private static Iec61850ReportContinuitySnapshot Snapshot(Iec61850ReportContinuityState state) =>
        Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["rcb"] = state }, 0, 0);

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var full = Path.Combine(directory.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
