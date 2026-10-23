namespace ArIED61850Tester.Models;

public sealed class NativeReportInventory
{
    public List<NativeDataSetCandidate> DataSets { get; set; } = new();
    public List<NativeReportControlCandidate> ReportControls { get; set; } = new();

    public int BufferedCount => ReportControls.Count(x => x.Buffered);
    public int UnbufferedCount => ReportControls.Count(x => !x.Buffered);
    public string Summary => $"DataSets={DataSets.Count}, RCB={ReportControls.Count} (BRCB={BufferedCount}, URCB={UnbufferedCount})";
}
