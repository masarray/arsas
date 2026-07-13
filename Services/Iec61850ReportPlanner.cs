using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public static class Iec61850ReportPlanner
{
    private const int DynamicDataSetChunkSize = 96;

    public static IReadOnlyList<ReportControlPlan> BuildPlans(
        Iec61850MonitorDevice device,
        IEnumerable<Iec61850MonitorPoint> points,
        bool? allowDynamicDataSetWrites = null)
    {
        var all = points.ToList();
        if (all.Count == 0)
            return Array.Empty<ReportControlPlan>();

        var dynamicWritesAllowed = allowDynamicDataSetWrites ?? device.AllowDynamicDataSetWrites;
        var plans = new List<ReportControlPlan>();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Static discovery hints are candidates only. Exact coverage is confirmed later
        // from the DataSet members returned by the native report subscription planner.
        var staticGroups = all
            .Where(point => !string.IsNullOrWhiteSpace(point.ReportControlReference) ||
                            !string.IsNullOrWhiteSpace(point.DataSetReference))
            .GroupBy(BuildStaticKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in staticGroups)
        {
            var members = group.ToList();
            var first = members[0];
            var plan = CreateBasePlan(device, members, allowDynamicDataSetWrites: dynamicWritesAllowed);
            plan.ReportControlReference = first.ReportControlReference;
            plan.DataSetReference = first.DataSetReference;
            plan.Buffered = LooksBuffered(first.ReportControlReference);
            plan.Mode = dynamicWritesAllowed
                ? "Smart Auto: static report → dynamic report → polling fallback"
                : "Static report preferred + polling fallback";
            plan.Status = "Static candidate";
            plans.Add(plan);
            foreach (var member in members)
                assigned.Add(member.PointKey);
        }

        // Smart Auto uses a temporary association-scoped DataSet/URCB when existing
        // static report coverage is unavailable. The native engine cleans it up when
        // monitoring stops; polling remains the final fallback.
        if (dynamicWritesAllowed)
        {
            var dynamicMembers = all
                .Where(point => !assigned.Contains(point.PointKey))
                .ToList();

            foreach (var chunk in Chunk(dynamicMembers, DynamicDataSetChunkSize))
            {
                var plan = CreateBasePlan(device, chunk, allowDynamicDataSetWrites: true);
                plan.Mode = "Smart Auto: dynamic DataSet/URCB → polling fallback";
                plan.Status = "Dynamic candidate";
                plans.Add(plan);
            }
        }

        return plans
            .OrderByDescending(plan => plan.Buffered)
            .ThenByDescending(plan => plan.FastStatusCount)
            .ThenByDescending(plan => plan.BindingCount)
            .ToList();
    }


    public static IReadOnlyList<ReportControlPlan> BuildDynamicFallbackPlans(
        Iec61850MonitorDevice device,
        IEnumerable<Iec61850MonitorPoint> points)
    {
        var members = points
            .GroupBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (!device.AllowDynamicDataSetWrites || members.Count == 0)
            return Array.Empty<ReportControlPlan>();

        return Chunk(members, DynamicDataSetChunkSize)
            .Select(chunk =>
            {
                var plan = CreateBasePlan(device, chunk, allowDynamicDataSetWrites: true);
                plan.Mode = "Smart Auto recovery: dynamic DataSet/URCB → polling fallback";
                plan.Status = "Dynamic candidate";
                return plan;
            })
            .ToList();
    }

    private static ReportControlPlan CreateBasePlan(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<Iec61850MonitorPoint> members,
        bool allowDynamicDataSetWrites)
    {
        return new ReportControlPlan
        {
            RelayId = device.DeviceId,
            RelayName = device.Name,
            RelayIpAddress = device.IpAddress,
            IedName = device.Name,
            // Preserve the IED's existing report configuration by default. A zero value
            // means the native engine should read/use the configured IntgPd rather than
            // forcing a five-second engineering value.
            IntegrityPeriodMs = 0,
            TriggerOptions = "dchg qchg dupd integrity GI",
            OptionalFields = "sequence-number report-timestamp reason-for-inclusion data-set data-reference conf-revision",
            AllowDynamicDataSetWrites = allowDynamicDataSetWrites,
            Bindings = members
                .OrderByDescending(IsFastPoint)
                .ThenBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string BuildStaticKey(Iec61850MonitorPoint point)
        => $"{point.ReportControlReference}|{point.DataSetReference}";

    private static bool LooksBuffered(string? reference)
    {
        var text = reference ?? string.Empty;
        return text.Contains("BRCB", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$BR$", StringComparison.OrdinalIgnoreCase) ||
               text.Contains(".BR.", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("buffer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFastPoint(Iec61850MonitorPoint point)
    {
        var reference = Normalize(point.IecReference);
        return point.Category.Equals("Position", StringComparison.OrdinalIgnoreCase) ||
               point.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase) ||
               point.Category.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
               point.IecDataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               point.IecDataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".pos.stval") ||
               reference.EndsWith(".general");
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var offset = 0; offset < source.Count; offset += size)
            yield return source.Skip(offset).Take(size).ToList();
    }

    private static string Normalize(string? reference)
        => (reference ?? string.Empty).Replace('$', '.').ToLowerInvariant();
}
