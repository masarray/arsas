namespace ArIED61850Tester.Models;

public sealed class ReportControlPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");
    public string RelayId { get; set; } = string.Empty;
    public string RelayName { get; set; } = string.Empty;
    public string RelayIpAddress { get; set; } = string.Empty;
    public string IedName { get; set; } = string.Empty;
    public string ReportControlReference { get; set; } = string.Empty;
    public string DataSetReference { get; set; } = string.Empty;
    public string Mode { get; set; } = "Smart Auto reporting + polling fallback";
    public bool AllowDynamicDataSetWrites { get; set; }
    public bool Buffered { get; set; }
    public string ReportId { get; set; } = string.Empty;
    public int IntegrityPeriodMs { get; set; }
    public string TriggerOptions { get; set; } = string.Empty;
    public string OptionalFields { get; set; } = string.Empty;
    public string Status { get; set; } = "Planned";
    public List<Iec61850MonitorPoint> Bindings { get; set; } = new();

    public int BindingCount => Bindings.Count;
    public int FastStatusCount => Bindings.Count(IsFastStatusCandidate);
    public string DisplayReference => !string.IsNullOrWhiteSpace(ReportControlReference)
        ? ReportControlReference
        : !string.IsNullOrWhiteSpace(DataSetReference)
            ? DataSetReference
            : "Dynamic DataSet";
    public string Summary => $"{IedNameOrRelay()} • {BindingCount} point(s) • {(Buffered ? "BRCB" : "URCB/RCB")} • {DisplayReference}";

    private string IedNameOrRelay()
    {
        if (!string.IsNullOrWhiteSpace(IedName)) return IedName;
        if (!string.IsNullOrWhiteSpace(RelayName)) return RelayName;
        return string.IsNullOrWhiteSpace(RelayIpAddress) ? "IED" : RelayIpAddress;
    }

    private static bool IsFastStatusCandidate(Iec61850MonitorPoint point)
    {
        var category = point.Category ?? string.Empty;
        var dataType = point.IecDataType ?? string.Empty;
        var reference = (point.IecReference ?? string.Empty).Replace('$', '.').ToLowerInvariant();

        return category.Equals("Position", StringComparison.OrdinalIgnoreCase) ||
               category.Equals("Protection", StringComparison.OrdinalIgnoreCase) ||
               category.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
               dataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               dataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".pos.stval") ||
               reference.Contains("xcbr") ||
               reference.Contains("xswi") ||
               reference.Contains("cswi") ||
               reference.EndsWith(".general");
    }
}
