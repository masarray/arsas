using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109YEventProvenanceTests
{
    [Theory]
    [InlineData("data-change", "Static DataSet: StaticBrcb", "Spont", "BRCB")]
    [InlineData("dchg", "Static DataSet: StaticUrcb", "Spont", "URCB")]
    [InlineData("quality-change", "Static DataSet: StaticBrcb", "Qchg", "BRCB")]
    [InlineData("dupd", "Static DataSet: StaticBrcb", "DUpd", "BRCB")]
    [InlineData("general-interrogation", "Static DataSet: StaticBrcb", "GI", "BRCB")]
    [InlineData("integrity", "Static DataSet: StaticBrcb", "Intg", "BRCB")]
    [InlineData("general-interrogation, data-change", "Static DataSet: StaticBrcb", "GI", "BRCB")]
    [InlineData("cyclic", "MMS Polling", "Poll", "MMS read")]
    [InlineData("report / reason not supplied", "Static DataSet: StaticBrcb", "Chg", "BRCB")]
    public void EventLabelsAreShortAndUseActualReasonOnly(
        string reason, string acquisition, string expectedCause, string expectedStream)
    {
        var e = new Iec61850EventEntry { Reason = reason, SourceMode = acquisition };
        Assert.Equal(expectedCause, e.EventCause);
        Assert.Equal(expectedStream, e.ReportChannel);
        Assert.True(e.EventCause.Length <= 5);
    }

    [Fact]
    public void DecodedReportIdWinsOverRcbNameAndGenericStaticMode()
    {
        var entry = new Iec61850EventEntry
        {
            Reason = "data-change",
            SourceMode = "Static DataSet: StaticUrcb",
            ReportId = "urcbA01",
            ReportControlReference = "BCUGELD/LLN0$RP$Unbuffer01"
        };
        Assert.Equal("Spont", entry.EventCause);
        Assert.Equal("urcbA01", entry.AcquisitionShort);
        Assert.Equal("BCUGELD/LLN0$RP$Unbuffer01", entry.ReportControlReference);
    }

    [Theory]
    [InlineData("ZIVLD/LLN0$BR$Buffer01", "Buffer01")]
    [InlineData("ZIVLD/LLN0.BR.Buffer02", "Buffer02")]
    [InlineData("F650LD/LLN0$RP$urcbA01", "urcbA01")]
    public void MissingReportIdFallsBackToActualMatchedRcbName(string reference, string expected)
    {
        var entry = new Iec61850EventEntry
        {
            SourceMode = "Static DataSet: StaticBrcb",
            ReportControlReference = reference
        };
        Assert.Equal(expected, entry.AcquisitionShort);
    }

    [Fact]
    public void MissingWireIdentityNeverFabricatesReportIdOrBufferedReplay()
    {
        var entry = new Iec61850EventEntry
        {
            Reason = "report / reason not supplied",
            SourceMode = "Static DataSet: StaticBrcb"
        };
        Assert.Equal("BRCB", entry.AcquisitionShort);
        Assert.Equal("Chg", entry.EventCause);
        Assert.DoesNotContain("replay", entry.EventCause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buffered", entry.EventCause, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, entry.ReportId);
    }

    [Theory]
    [InlineData("MMS Polling")]
    [InlineData("Static DataSet: StaticBrcb + MMS fallback")]
    public void PollingNeverClaimsThePreviouslyConfiguredReportIdentity(string acquisition)
    {
        var entry = new Iec61850EventEntry
        {
            SourceMode = acquisition,
            ReportId = "old-report",
            ReportControlReference = "IED/LLN0$BR$OldRcb"
        };
        Assert.Equal("MMS Polling", entry.AcquisitionShort);
        Assert.Equal("Poll", entry.EventCause);
    }

    [Fact]
    public void EventGridIsCompactAndCsvRetainsFullProvenance()
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
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var eventStart = xaml.IndexOf("<!-- EVENT LOG -->", StringComparison.Ordinal);
        var eventEnd = xaml.IndexOf("<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->", eventStart, StringComparison.Ordinal);
        Assert.True(eventStart >= 0 && eventEnd > eventStart);
        var visible = xaml[eventStart..eventEnd];

        Assert.Contains("Header=\"Event\" Binding=\"{Binding EventCause}\"", visible);
        Assert.Contains("Header=\"Acquisition\" Binding=\"{Binding AcquisitionShort}\"", visible);
        Assert.DoesNotContain("Header=\"Stream\"", visible);
        Assert.DoesNotContain("Header=\"Raw Reason\"", visible);
        Assert.Contains("Csv(entry.EventCause)", code);
        Assert.Contains("Csv(entry.AcquisitionShort)", code);
        Assert.Contains("Csv(entry.ReportChannel)", code);
        Assert.Contains("Csv(entry.SourceMode)", code);
        Assert.Contains("Csv(entry.Reason)", code);
        Assert.Contains("Csv(entry.ReportId)", code);
        Assert.Contains("Csv(entry.ReportControlReference)", code);

        Assert.Contains("reportId: update.ReportId", runtime);
        Assert.Contains("reportControlReference: string.IsNullOrWhiteSpace(update.ReportControlReference)", runtime);
        Assert.Contains("ReportId = trustReportEdge ? reportId", runtime);
        Assert.Contains("ReportControlReference = trustReportEdge ? reportControlReference", runtime);
        Assert.Contains("EventRaised?.Invoke(entry)", runtime);
        Assert.Contains("Events.TrimStart(10000)", code);
    }
}
