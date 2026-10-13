using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109YEventProvenanceTests
{
    [Theory]
    [InlineData("data-change", "Static DataSet: StaticBrcb", "Spontaneous", "BRCB")]
    [InlineData("dchg", "Static DataSet: StaticUrcb", "Spontaneous", "URCB")]
    [InlineData("quality-change", "Static DataSet: StaticBrcb", "Quality change", "BRCB")]
    [InlineData("dupd", "Static DataSet: StaticBrcb", "Data update", "BRCB")]
    [InlineData("general-interrogation", "Static DataSet: StaticBrcb", "General interrogation", "BRCB")]
    [InlineData("integrity", "Static DataSet: StaticBrcb", "Integrity", "BRCB")]
    [InlineData("general-interrogation, data-change", "Static DataSet: StaticBrcb", "General interrogation", "BRCB")]
    [InlineData("cyclic", "MMS Polling", "Observed change", "MMS read")]
    [InlineData("report / reason not supplied", "Static DataSet: StaticBrcb", "State change (cause unknown)", "BRCB")]
    public void EventLabelsOnlyUseActualReasonAndStream(string reason, string acquisition, string expectedCause, string expectedStream)
    {
        var e = new Iec61850EventEntry { Reason = reason, SourceMode = acquisition };
        Assert.Equal(expectedCause, e.EventCause);
        Assert.Equal(expectedStream, e.ReportChannel);
    }

    [Fact]
    public void BufferCapableReportAloneDoesNotPretendToProveReplayedHistory()
    {
        var e = new Iec61850EventEntry
        {
            Reason = "report / reason not supplied",
            SourceMode = "Static DataSet: StaticBrcb"
        };
        Assert.Equal("BRCB", e.ReportChannel);
        Assert.DoesNotContain("buffer", e.EventCause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replay", e.EventCause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spontaneous", e.EventCause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventGridAndCsvKeepCauseProvenanceVisible()
    {
        string Read(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var file = Path.Combine(dir.FullName, name);
                if (File.Exists(file)) return File.ReadAllText(file);
                dir = dir.Parent;
            }
            throw new FileNotFoundException(name);
        }

        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("Header=\"Event\" Binding=\"{Binding EventCause}\"", xaml);
        Assert.Contains("Header=\"Stream\" Binding=\"{Binding ReportChannel}\"", xaml);
        Assert.Contains("Header=\"Raw Reason\" Binding=\"{Binding Reason}\"", xaml);
        Assert.Contains("Csv(entry.EventCause)", code);
        Assert.Contains("Csv(entry.ReportChannel)", code);
        Assert.Contains("Csv(entry.Reason)", code);
        Assert.Contains("Runtime_EventRaised(Iec61850EventEntry entry)", code);
        Assert.Contains("Events.TrimStart(10000)", code);
    }
}
