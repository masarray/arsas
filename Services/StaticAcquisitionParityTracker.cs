using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Builds a stable semantic fingerprint for Static DataSet acquisition and compares
/// Discovery vs Open-SCL ingress without confusing concrete live RCB slot ownership with
/// configuration equivalence.
/// </summary>
internal static class StaticAcquisitionParityTracker
{
    public static StaticAcquisitionParitySnapshot Record(
        Iec61850MonitorDevice device,
        NativeHybridReportPlanningResult planning)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(planning);

        var ingress = ResolveIngress(device);
        if (ingress == StaticAcquisitionIngressKind.Unknown)
            return device.StaticAcquisitionParity;

        var evidence = BuildEvidence(device, planning, ingress);
        var previous = device.StaticAcquisitionParity;

        var discovery = ingress == StaticAcquisitionIngressKind.LiveDiscovery
            ? evidence
            : previous.Discovery;
        var openScl = ingress == StaticAcquisitionIngressKind.OpenScl
            ? evidence
            : previous.OpenScl;

        var updated = Compare(discovery, openScl);
        device.StaticAcquisitionParity = updated;
        return updated;
    }

    internal static StaticAcquisitionIngressEvidence BuildEvidence(
        Iec61850MonitorDevice device,
        NativeHybridReportPlanningResult planning,
        StaticAcquisitionIngressKind ingress)
    {
        var model = ingress == StaticAcquisitionIngressKind.OpenScl
            ? device.SclWorkspace?.DesignModel
            : device.LiveDiscoveryModel;

        var staticPlans = planning.ReportPlans
            .Where(plan =>
                plan.IsEngineAuthoritative &&
                plan.EngineAcquisitionKind.StartsWith("Static", StringComparison.OrdinalIgnoreCase))
            .OrderBy(plan => NormalizeReference(plan.DataSetReference), StringComparer.Ordinal)
            .ThenBy(plan => NormalizeKind(plan.EngineAcquisitionKind), StringComparer.Ordinal)
            .ToArray();

        // A matching hash must never establish parity when the engine did not provide
        // a complete, authoritative Static DataSet plan and ordered membership model.
        var evidenceIssues = new List<string>();
        if (!planning.IsAuthoritative)
            evidenceIssues.Add("planner was not authoritative");
        if (planning.RequestedPointCount <= 0)
            evidenceIssues.Add("no selected runtime points");
        if (planning.UncoveredSignalCount > 0)
            evidenceIssues.Add($"{planning.UncoveredSignalCount} uncovered runtime point(s)");
        if (staticPlans.Length == 0)
            evidenceIssues.Add("no engine-authoritative static report plans");
        if (model is null)
            evidenceIssues.Add("ingress model missing");

        var semanticLines = new List<string>
        {
            $"ied={NormalizeReference(device.Name)}|requested={planning.RequestedPointCount}|" +
            $"staticBrcbSignals={planning.StaticBrcbSignalCount}|staticUrcbSignals={planning.StaticUrcbSignalCount}|" +
            $"uncovered={planning.UncoveredSignalCount}|staticPlans={staticPlans.Length}"
        };

        var runtimeTargets = new List<string>();

        foreach (var plan in staticPlans)
        {
            var dataSetReference = NormalizeReference(plan.DataSetReference);
            var kind = NormalizeKind(plan.EngineAcquisitionKind);
            var dataSet = model?.DataSets.FirstOrDefault(candidate =>
                NormalizeReference(candidate.Reference).Equals(dataSetReference, StringComparison.Ordinal));

            var members = dataSet?.Members
                .OrderBy(member => member.Index)
                .Select(member =>
                    $"{member.Index}:{NormalizeReference(member.Reference)}@{NormalizeFc(member.FunctionalConstraint)}")
                .ToArray()
                ?? Array.Empty<string>();

            if (dataSet is null)
                evidenceIssues.Add($"DataSet {dataSetReference} missing from {ingress} model");
            else if (members.Length == 0)
                evidenceIssues.Add($"DataSet {dataSetReference} has no ordered member evidence");
            else if (dataSet.MemberCount > 0 && dataSet.MemberCount != members.Length)
                evidenceIssues.Add($"DataSet {dataSetReference} has {members.Length}/{dataSet.MemberCount} member descriptors");

            var selected = plan.Bindings
                .Where(point => !string.IsNullOrWhiteSpace(point.IecReference))
                .Select(point =>
                    $"{NormalizeReference(point.IecReference)}@{NormalizeFc(point.FunctionalConstraint)}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (selected.Length == 0)
                evidenceIssues.Add($"DataSet {dataSetReference} has no selected point bindings");

            semanticLines.Add(
                $"{kind}|dataset={dataSetReference}|members={members.Length}[{string.Join(",", members)}]|" +
                $"selected={selected.Length}[{string.Join(",", selected)}]");

            runtimeTargets.Add(
                $"{kind}|dataset={dataSetReference}|rcb={NormalizeReference(plan.ReportControlReference)}|" +
                $"bindings={plan.Bindings.Count}");
        }

        var canonicalText = string.Join("\n", semanticLines);
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)))
            .ToLowerInvariant();

        return new StaticAcquisitionIngressEvidence
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Ingress = ingress,
            IedName = device.Name,
            SemanticFingerprint = fingerprint,
            RequestedPointCount = planning.RequestedPointCount,
            PlannedReportCount = staticPlans.Length,
            StaticBrcbSignalCount = planning.StaticBrcbSignalCount,
            StaticUrcbSignalCount = planning.StaticUrcbSignalCount,
            UncoveredSignalCount = planning.UncoveredSignalCount,
            SemanticLines = semanticLines,
            RuntimeTargets = runtimeTargets,
            IsComparable = evidenceIssues.Count == 0,
            IncomparableReason = string.Join("; ", evidenceIssues)
        };
    }

    internal static StaticAcquisitionParitySnapshot Compare(
        StaticAcquisitionIngressEvidence? discovery,
        StaticAcquisitionIngressEvidence? openScl)
    {
        if (discovery is null && openScl is null)
            return new StaticAcquisitionParitySnapshot();

        if (discovery is null || openScl is null)
        {
            return new StaticAcquisitionParitySnapshot
            {
                Discovery = discovery,
                OpenScl = openScl,
                Status = StaticAcquisitionParityStatus.AwaitingOtherIngress
            };
        }

        if (!discovery.IsComparable || !openScl.IsComparable)
        {
            var issues = new List<string>();
            if (!discovery.IsComparable)
                issues.Add($"Discovery: {discovery.IncomparableReason}");
            if (!openScl.IsComparable)
                issues.Add($"Open SCL: {openScl.IncomparableReason}");
            return new StaticAcquisitionParitySnapshot
            {
                Discovery = discovery,
                OpenScl = openScl,
                Status = StaticAcquisitionParityStatus.InsufficientEvidence,
                Differences = issues
            };
        }

        if (discovery.SemanticFingerprint.Equals(
                openScl.SemanticFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return new StaticAcquisitionParitySnapshot
            {
                Discovery = discovery,
                OpenScl = openScl,
                Status = StaticAcquisitionParityStatus.Equivalent
            };
        }

        var discoveryLines = discovery.SemanticLines.ToHashSet(StringComparer.Ordinal);
        var sclLines = openScl.SemanticLines.ToHashSet(StringComparer.Ordinal);
        var differences = discoveryLines
            .Except(sclLines, StringComparer.Ordinal)
            .Select(line => $"Discovery only: {line}")
            .Concat(sclLines
                .Except(discoveryLines, StringComparer.Ordinal)
                .Select(line => $"Open SCL only: {line}"))
            .Take(12)
            .ToArray();

        return new StaticAcquisitionParitySnapshot
        {
            Discovery = discovery,
            OpenScl = openScl,
            Status = StaticAcquisitionParityStatus.Mismatch,
            Differences = differences
        };
    }

    private static StaticAcquisitionIngressKind ResolveIngress(Iec61850MonitorDevice device)
    {
        if (device.SclWorkspace?.DesignModel is not null)
            return StaticAcquisitionIngressKind.OpenScl;
        if (device.LiveDiscoveryModel is not null)
            return StaticAcquisitionIngressKind.LiveDiscovery;
        return StaticAcquisitionIngressKind.Unknown;
    }

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();

    private static string NormalizeKind(string? value)
    {
        var kind = (value ?? string.Empty).Trim();
        if (kind.Contains("brcb", StringComparison.OrdinalIgnoreCase))
            return "static-brcb";
        if (kind.Contains("urcb", StringComparison.OrdinalIgnoreCase))
            return "static-urcb";
        return "static-report";
    }

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
