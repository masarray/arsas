using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class ControlFatEvidenceExportTests
{
    [Fact]
    public void EvidenceSnapshot_IsDeterministic_AndLabelsWireVsIndependentFeedback()
    {
        var e = new Iec61850ControlFatEvidence(
            new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero),
            "IED-A • 192.0.2.10:102", "LD0/CSWI1.Pos", "DPC", "SBO enhanced",
            "Open", "Closed [10]", "StationControl", "ARSAS",
            true, false, false, "Feedback not verified",
            "IED accepted; independent process feedback not verified.",
            "23", "Positive", "SBOw ✓ → Operate ✓ → Termination ✓",
            "LastApplError: none\nMMS request: A1");
        var text = e.ToClipboardText();
        Assert.Contains("2026-10-23T08:00:00.0000000+00:00", text);
        Assert.Contains("LD0/CSWI1.Pos", text);
        Assert.Contains("Requested value: Closed [10]", text);
        Assert.Contains("Origin: StationControl / ARSAS", text);
        Assert.Contains("ctlNum: 23", text);
        Assert.Contains("CommandTermination: Positive", text);
        Assert.Contains("MMS steps (evidence only)", text);
        Assert.Contains("LastApplError: none", text);
        Assert.Contains("MMS service acceptance does not prove primary equipment movement", text);
    }

    [Fact]
    public void SingleLineIdentifiers_CannotImpersonateOtherEvidenceFields()
    {
        var e = new Iec61850ControlFatEvidence(
            DateTimeOffset.UnixEpoch, "IED-1\nResult: confirmed",
            "LD0/CSWI1.Pos", "DPC", "SBO", "", "",
            "StationControl", "ARSAS", true, false, false,
            "Not sent", "No command", "Auto", "—", "", "");
        var output = e.ToClipboardText();
        Assert.Contains("IED: IED-1 Result: confirmed", output);
        Assert.DoesNotContain("IED-1\nResult: confirmed", output);
    }

    [Fact]
    public void PopupCopyEvidence_IsDisabledDuringFlightAndPerformsNoMmsIo()
    {
        var markup = ReadSource("ControlCommandWindow.xaml");
        XDocument.Parse(markup);
        Assert.Contains("Content=\"Copy evidence\"", markup);
        Assert.Contains("Click=\"CopyEvidence_Click\"", markup);
        Assert.Contains("IsEnabled=\"{Binding CanCopyEvidence}\"", markup);
        var source = ReadSource("ControlCommandWindow.xaml.cs");
        Assert.Contains("Clipboard.SetText(snapshot.ToClipboardText())", source);
        Assert.Contains("!_commandFlightActive", source);
        Assert.Contains("ControlEvidenceText = string.Empty; // Do not export stale association evidence.", source);
        var handler = source.Split("private void CopyEvidence_Click(")[1]
            .Split("private void Close_Click(", StringSplitOptions.None)[0];
        Assert.DoesNotContain("ExecuteControlAsync(", handler);
        Assert.DoesNotContain("InspectControlAsync(", handler);
        Assert.DoesNotContain("OperateAsync(", handler);
        Assert.DoesNotContain("WriteBrowser", handler);
        Assert.DoesNotContain("CancelAsync(", handler);
        Assert.DoesNotContain("SelectAsync(", handler);
    }

    private static string ReadSource(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, file);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(file);
    }
}
