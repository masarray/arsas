using System.Xml.Linq;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M2IedBrowserFilesTests
{
    private static Iec61850FaultRecordSet Record(
        string name = "FAULT001", string path = "/COMTRADE/FAULT001",
        bool complete = true, bool withCfg = true)
    {
        var files = new List<Iec61850FaultRecordFile>
        {
            new() { Name = name + ".dat", RemotePath = path + ".dat",
                SizeBytes = 5, Kind = Iec61850FaultRecordFileKind.Data }
        };
        if (withCfg)
            files.Add(new Iec61850FaultRecordFile
            {
                Name = name + ".cfg", RemotePath = path + ".cfg",
                SizeBytes = 4, Kind = Iec61850FaultRecordFileKind.Configuration
            });
        return new Iec61850FaultRecordSet
        {
            BaseName = name, RecordId = path,
            Files = files, IsComplete = complete,
            Completeness = complete ? "Complete" : "Missing COMTRADE member"
        };
    }

    [Fact]
    public void Catalog_UsesFullEngineRecordIdentity_NotExtensionHeuristics()
    {
        var record = Record();
        var index = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog
        {
            Records = [record]
        });
        Assert.Equal(2, index.Files.Count);
        Assert.True(index.TryGetRecord("/COMTRADE/FAULT001.dat", out var found, out _));
        Assert.Same(record, found);
        Assert.True(index.TryGetRecord("/COMTRADE/FAULT001.cfg", out found, out _));
        Assert.Same(record, found);
        Assert.False(index.TryGetRecord("/COMTRADE/FAULT001.datx", out _, out _));
        Assert.Equal("FAULT001.cfg", index.Files[0].Name);
    }

    [Fact]
    public void AmbiguousRemoteIdentity_FailsClosedEvenIfFileNamesLookEqual()
    {
        var a = Record(path: "/LD1/FAULT001");
        var b = Record(path: "/ld1/FAULT001");
        var index = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog
        {
            Records = [a, b]
        });
        Assert.False(index.TryGetRecord("/LD1/FAULT001.cfg", out _, out var reason));
        Assert.Contains("Ambiguous", reason);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IncompleteOrMissingCfg_RejectsNativeOpen(bool complete, bool withCfg)
    {
        var index = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog
        {
            Records = [Record(complete: complete, withCfg: withCfg)]
        });
        Assert.False(index.TryGetRecord("/COMTRADE/FAULT001.dat", out _, out _));
    }

    [Fact]
    public void CompleteLocalBundle_RequiresAllMembersAndDeclaredSizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "arsas-files-m2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var record = Record();
            var dir = Directory.CreateDirectory(Path.Combine(root, "FAULT001")).FullName;
            File.WriteAllBytes(Path.Combine(dir, "FAULT001.cfg"), new byte[4]);
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
            File.WriteAllBytes(Path.Combine(dir, "FAULT001.dat"), new byte[6]);
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
            File.WriteAllBytes(Path.Combine(dir, "FAULT001.dat"), new byte[5]);
            Assert.True(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out var actual));
            Assert.Equal(dir, actual);
            File.Delete(Path.Combine(dir, "FAULT001.dat"));
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SimilarNamedDirectory_CannotMasqueradeAsCompleteRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "arsas-files-m2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "FAULT001-malicious")).FullName;
            File.WriteAllBytes(Path.Combine(dir, "FAULT001.cfg"), new byte[4]);
            File.WriteAllBytes(Path.Combine(dir, "FAULT001.dat"), new byte[5]);
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(Record(), root, out _));
            Directory.Move(dir, Path.Combine(root, "FAULT001-1"));
            Assert.True(IedBrowserLocalRecordEvidence.TryFindComplete(Record(), root, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FreshTransfer_GatesOnEveryLocalPathAndByteCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "arsas-files-m2-" + Guid.NewGuid().ToString("N"));
        var dir = Directory.CreateDirectory(Path.Combine(root, "FAULT001")).FullName;
        try
        {
            var cfg = Path.Combine(dir, "FAULT001.cfg");
            var dat = Path.Combine(dir, "FAULT001.dat");
            File.WriteAllBytes(cfg, new byte[4]);
            File.WriteAllBytes(dat, new byte[5]);
            var ok = new Iec61850FaultRecordDownloadResult
            {
                IsSuccess = true,
                DestinationDirectory = dir,
                Files =
                [
                    new() { RemotePath = "/COMTRADE/FAULT001.dat", LocalPath = dat },
                    new() { RemotePath = "/COMTRADE/FAULT001.cfg", LocalPath = cfg }
                ]
            };
            Assert.True(IedBrowserLocalRecordEvidence.ValidateFresh(Record(), ok, out _));
            File.WriteAllBytes(dat, new byte[6]);
            Assert.False(IedBrowserLocalRecordEvidence.ValidateFresh(Record(), ok, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NativeFilesGrid_ExposesThreeColumns_AndBoundedActions()
    {
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        var p = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var grid = Assert.Single(xaml.Descendants(p + "DataGrid"),
            e => e.Attribute(x + "Name")?.Value == "IedBrowserFilesGrid");
        var cols = grid.Descendants(p + "DataGridTextColumn")
            .Select(c => (string?)c.Attribute("Header")).ToArray();
        Assert.Equal(["Filename", "Time", "Size"], cols);
        Assert.Equal("True", (string?)grid.Attribute("EnableRowVirtualization"));
        Assert.Contains("IedBrowserFile_DoubleClick", grid.ToString(), StringComparison.Ordinal);
        Assert.Contains("IedBrowserFile_LoadingRow", grid.ToString(), StringComparison.Ordinal);
        var overlay = Assert.Single(xaml.Descendants(p + "Border"),
            e => e.Attribute(x + "Name")?.Value == "IedBrowserFileTransferOverlay");
        Assert.Contains("IedBrowserCancelFileTransfer_Click", overlay.ToString(), StringComparison.Ordinal);

        var source = File.ReadAllText(Find("MainWindow.IedBrowser.Files.cs"));
        Assert.Contains("new FaultRecordTransferClient()", source, StringComparison.Ordinal);
        Assert.Contains("download.Click += IedBrowserFileDownload_Click", source, StringComparison.Ordinal);
        Assert.Contains("IedBrowserLocalRecordEvidence.ValidateFresh", source, StringComparison.Ordinal);
        Assert.Contains("ArdIrecViewerLauncher.TryResolveComtradeCfg", source, StringComparison.Ordinal);
        Assert.Contains("ArdIrecNativeBridge.TryOpen", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex != IedBrowserWorkspaceIndex", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateRcb(", source, StringComparison.Ordinal);
    }

    private static string Find(string filename)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, filename);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(filename);
    }
}
