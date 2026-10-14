using System.Xml.Linq;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M2IedBrowserFileSelectionAndProvenanceTests
{
    private static Iec61850FaultRecordSet Record(string id = "/event/ligne_1",
        DateTimeOffset? time = null)
    {
        var at = time ?? new DateTimeOffset(2023, 11, 7, 18, 25, 0, TimeSpan.Zero);
        return new Iec61850FaultRecordSet
        {
            RecordId = id,
            BaseName = "ligne_1",
            IsComplete = true,
            Completeness = "CFG + DAT",
            Files =
            [
                new Iec61850FaultRecordFile
                {
                    Name = "ligne_1.CFG", RemotePath = id + ".CFG",
                    SizeBytes = 4, LastModifiedUtc = at,
                    Kind = Iec61850FaultRecordFileKind.Configuration
                },
                new Iec61850FaultRecordFile
                {
                    Name = "ligne_1.DAT", RemotePath = id + ".DAT",
                    SizeBytes = 5, LastModifiedUtc = at,
                    Kind = Iec61850FaultRecordFileKind.Data
                }
            ]
        };
    }

    private static (string root, string dir) Prepare(string outerRoot, string host, string name = "IED-A")
    {
        var root = IedBrowserRecordProvenanceCache.DeviceRoot(outerRoot, name, host, 102);
        var directory = Directory.CreateDirectory(Path.Combine(root, "ligne_1")).FullName;
        File.WriteAllBytes(Path.Combine(directory, "ligne_1.CFG"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(directory, "ligne_1.DAT"), [5, 6, 7, 8, 9]);
        return (root, directory);
    }

    [Fact]
    public void Provenance_SeparatesDifferentIedsAndEndpointsEvenForSameFilenames()
    {
        var outer = Path.Combine(Path.GetTempPath(), "arsas-m2-5b-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (a, dir) = Prepare(outer, "192.168.81.102");
            var b = IedBrowserRecordProvenanceCache.DeviceRoot(outer, "IED-B", "192.168.81.102", 102);
            var c = IedBrowserRecordProvenanceCache.DeviceRoot(outer, "IED-A", "192.168.81.103", 102);
            Assert.NotEqual(a, b);
            Assert.NotEqual(a, c);
            IedBrowserRecordProvenanceCache.RecordVerifiedTransfer(Record(), a, dir);
            Assert.True(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), a, out var actual));
            Assert.Equal(dir, actual);
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), b, out _));
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), c, out _));
        }
        finally { if (Directory.Exists(outer)) Directory.Delete(outer, recursive: true); }
    }

    [Fact]
    public void Provenance_FailsClosedForChangedRecordIdOrRemoteModificationTime()
    {
        var outer = Path.Combine(Path.GetTempPath(), "arsas-m2-5b-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (root, dir) = Prepare(outer, "192.168.81.102");
            IedBrowserRecordProvenanceCache.RecordVerifiedTransfer(Record(), root, dir);
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(
                Record(id: "/another/ligne_1"), root, out _));
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(
                Record(time: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)), root, out _));
        }
        finally { if (Directory.Exists(outer)) Directory.Delete(outer, recursive: true); }
    }

    [Fact]
    public void Provenance_FailsClosedForSameLengthLocalCorruptionAndBrokenManifest()
    {
        var outer = Path.Combine(Path.GetTempPath(), "arsas-m2-5b-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (root, dir) = Prepare(outer, "192.168.81.102");
            IedBrowserRecordProvenanceCache.RecordVerifiedTransfer(Record(), root, dir);
            Assert.True(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), root, out _));
            File.WriteAllBytes(Path.Combine(dir, "ligne_1.DAT"), [0, 0, 0, 0, 0]);
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), root, out _));
            File.WriteAllText(Path.Combine(dir, ".arsas-browser-record.json"), "{ invalid");
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(Record(), root, out _));
        }
        finally { if (Directory.Exists(outer)) Directory.Delete(outer, recursive: true); }
    }

    [Fact]
    public void Provenance_UnknownRemoteModificationCannotProveFreshness()
    {
        var outer = Path.Combine(Path.GetTempPath(), "arsas-m2-5b-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (root, dir) = Prepare(outer, "192.168.81.102");
            var original = Record();
            IedBrowserRecordProvenanceCache.RecordVerifiedTransfer(original, root, dir);
            var unknown = new Iec61850FaultRecordSet
            {
                RecordId = original.RecordId,
                BaseName = original.BaseName,
                IsComplete = original.IsComplete,
                Files = original.Files.Select(f => new Iec61850FaultRecordFile
                {
                    Name = f.Name, RemotePath = f.RemotePath,
                    SizeBytes = f.SizeBytes, LastModifiedUtc = null, Kind = f.Kind
                }).ToArray()
            };
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(unknown, root, out _));
        }
        finally { if (Directory.Exists(outer)) Directory.Delete(outer, recursive: true); }
    }

    [Fact]
    public void NativeFiles_VisibleSelectionAndDownloadFolderWorkflowHaveExplicitContracts()
    {
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var grid = Assert.Single(xaml.Descendants(p + "DataGrid"),
            e => e.Attribute(x + "Name")?.Value == "IedBrowserFilesGrid");
        Assert.Equal("FullRow", (string?)grid.Attribute("SelectionUnit"));
        Assert.Contains("IedBrowserFiles_SelectionChanged", grid.ToString(), StringComparison.Ordinal);
        Assert.Contains("IsSelected", grid.ToString(), StringComparison.Ordinal);
        Assert.Contains("#2563EB", grid.ToString(), StringComparison.Ordinal);
        var toolbar = Assert.Single(xaml.Descendants(p + "Button"),
            e => e.Attribute(x + "Name")?.Value == "IedBrowserFilesDownloadSelectedButton");
        Assert.Equal("IedBrowserDownloadSelected_Click", (string?)toolbar.Attribute("Click"));

        var source = File.ReadAllText(Find("MainWindow.IedBrowser.Files.cs"));
        Assert.Contains("rows.FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("previousPath", source, StringComparison.Ordinal);
        Assert.Contains("IedBrowserFilesGrid.SelectedItem = file", source, StringComparison.Ordinal);
        Assert.Contains("OpenIedBrowserDownloadedFolder(localDirectory);", source,
            StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
        Assert.Contains("IedBrowserRecordProvenanceCache.TryFindVerified", source,
            StringComparison.Ordinal);
        Assert.Contains("IedBrowserRecordProvenanceCache.RecordVerifiedTransfer", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IedBrowserLocalRecordEvidence.TryFindComplete(", source,
            StringComparison.Ordinal);
    }

    private static string Find(string relative)
    {
        for (DirectoryInfo? cursor = new(AppContext.BaseDirectory); cursor is not null;
             cursor = cursor.Parent)
        {
            var match = Path.Combine(cursor.FullName, relative);
            if (File.Exists(match)) return match;
        }
        throw new FileNotFoundException(relative);
    }
}
