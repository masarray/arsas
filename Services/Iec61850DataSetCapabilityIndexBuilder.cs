using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Builds an immutable, source-neutral view of static DataSet/report capabilities.
///
/// Open SCL and Smart Discovery both produce LiveIedModelDiscoveryDocument. This worker
/// intentionally consumes only stable configuration facts from that shared model so the
/// two sources can be compared deterministically. Runtime-mutating RCB state such as
/// RptEna, reservation and owner state is deliberately excluded from fingerprints.
/// </summary>
public static class Iec61850DataSetCapabilityIndexBuilder
{
    public static Task<Iec61850DataSetCapabilityIndex> BuildAsync(
        LiveIedModelDiscoveryDocument model,
        long generation,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Task.Run(
            () => Build(model, generation, source, cancellationToken),
            cancellationToken);
    }

    public static Iec61850DataSetCapabilityIndex Build(
        LiveIedModelDiscoveryDocument model,
        long generation,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var reportCapabilities = model.ReportControls
            .Select(ToReportCapability)
            .OrderBy(report => report.Reference, StringComparer.Ordinal)
            .ThenBy(report => report.DataSetReference, StringComparer.Ordinal)
            .ToArray();

        var reportsByDataSet = reportCapabilities
            .Where(report => !string.IsNullOrWhiteSpace(report.DataSetReference))
            .GroupBy(report => NormalizeReference(report.DataSetReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Iec61850ReportControlCapability>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var dataSets = new List<Iec61850DataSetCapability>(model.DataSets.Count);
        foreach (var dataSet in model.DataSets
                     .OrderBy(item => NormalizeReference(item.Reference), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = dataSet.Members
                .OrderBy(member => member.Index)
                .Select(member => new Iec61850DataSetMemberCapability
                {
                    Index = member.Index,
                    Reference = NormalizeReference(member.Reference),
                    FunctionalConstraint = NormalizeToken(member.FunctionalConstraint),
                    MmsReference = NormalizeReference(member.MmsReference)
                })
                .ToArray();

            var key = NormalizeReference(dataSet.Reference);
            reportsByDataSet.TryGetValue(key, out var reports);
            reports ??= Array.Empty<Iec61850ReportControlCapability>();

            dataSets.Add(new Iec61850DataSetCapability
            {
                Reference = key,
                Domain = NormalizeToken(dataSet.Domain),
                LogicalNode = NormalizeToken(dataSet.LogicalNode),
                Name = NormalizeToken(dataSet.Name),
                IsDeletable = dataSet.IsDeletable,
                Members = members,
                ReportControls = reports,
                Fingerprint = HashTokens(BuildDataSetTokens(dataSet, members))
            });
        }

        var dataSetFingerprint = HashTokens(
            dataSets.SelectMany(dataSet => new[]
            {
                dataSet.Reference,
                dataSet.Fingerprint
            }));

        var reportBindingFingerprint = HashTokens(
            reportCapabilities.SelectMany(BuildReportTokens));

        var modelFingerprint = HashTokens(new[]
        {
            NormalizeToken(model.SchemaVersion),
            NormalizeToken(model.IedName),
            NormalizeToken(model.AccessPointName),
            dataSetFingerprint,
            reportBindingFingerprint
        });

        return new Iec61850DataSetCapabilityIndex
        {
            Generation = generation,
            Source = NormalizeToken(source),
            IedName = NormalizeToken(model.IedName),
            AccessPointName = NormalizeToken(model.AccessPointName),
            ModelFingerprint = modelFingerprint,
            DataSetFingerprint = dataSetFingerprint,
            ReportBindingFingerprint = reportBindingFingerprint,
            DataSets = dataSets
        };
    }

    public static Iec61850DataSetCapabilityParity Compare(
        Iec61850DataSetCapabilityIndex left,
        Iec61850DataSetCapabilityIndex right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var dataSetsMatch = string.Equals(
            left.DataSetFingerprint,
            right.DataSetFingerprint,
            StringComparison.Ordinal);
        var reportBindingsMatch = string.Equals(
            left.ReportBindingFingerprint,
            right.ReportBindingFingerprint,
            StringComparison.Ordinal);

        return new Iec61850DataSetCapabilityParity
        {
            DataSetsMatch = dataSetsMatch,
            ReportBindingsMatch = reportBindingsMatch,
            Summary =
                $"DataSets={(dataSetsMatch ? "match" : "mismatch")}; " +
                $"ReportBindings={(reportBindingsMatch ? "match" : "mismatch")}; " +
                $"left={left.Source}[{left.DataSetCount} DS/{left.MemberCount} members], " +
                $"right={right.Source}[{right.DataSetCount} DS/{right.MemberCount} members]."
        };
    }

    private static Iec61850ReportControlCapability ToReportCapability(LiveIedReportControlModel report)
        => new()
        {
            Reference = NormalizeReference(report.Reference),
            DataSetReference = NormalizeReference(report.DataSetReference),
            Buffered = report.Buffered,
            Indexed = report.Indexed,
            ReportId = NormalizeToken(report.ReportId),
            ConfRev = NormalizeToken(report.ConfRev),
            TriggerOptions = NormalizeToken(report.TriggerOptions),
            OptionalFields = NormalizeToken(report.OptionalFields),
            BufferTimeMs = NormalizeToken(report.BufferTimeMs),
            IntegrityPeriodMs = NormalizeToken(report.IntegrityPeriodMs)
        };

    private static IEnumerable<string> BuildDataSetTokens(
        LiveIedDataSetModel dataSet,
        IReadOnlyList<Iec61850DataSetMemberCapability> members)
    {
        yield return "DataSet";
        yield return NormalizeReference(dataSet.Reference);
        yield return NormalizeToken(dataSet.Domain);
        yield return NormalizeToken(dataSet.LogicalNode);
        yield return NormalizeToken(dataSet.Name);
        yield return dataSet.IsDeletable?.ToString() ?? "null";
        yield return members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var member in members)
        {
            yield return member.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return member.Reference;
            yield return member.FunctionalConstraint;
            yield return member.MmsReference;
        }
    }

    private static IEnumerable<string> BuildReportTokens(Iec61850ReportControlCapability report)
    {
        yield return "ReportControl";
        yield return report.Reference;
        yield return report.DataSetReference;
        yield return report.Buffered ? "BRCB" : "URCB";
        yield return report.Indexed ? "indexed" : "exact";
        yield return report.ReportId;
        yield return report.ConfRev;
        yield return report.TriggerOptions;
        yield return report.OptionalFields;
        yield return report.BufferTimeMs;
        yield return report.IntegrityPeriodMs;
    }

    private static string HashTokens(IEnumerable<string> tokens)
    {
        var canonical = new StringBuilder();
        foreach (var token in tokens)
        {
            var value = token ?? string.Empty;
            canonical
                .Append(value.Length)
                .Append(':')
                .Append(value)
                .Append('|');
        }

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeToken(string? value)
        => (value ?? string.Empty).Trim();
}
