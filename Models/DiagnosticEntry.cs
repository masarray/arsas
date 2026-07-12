namespace ArIED61850Tester.Models;

public class DiagnosticEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INFO";
    public string Source { get; set; } = "System";
    public string Message { get; set; } = "";
}
