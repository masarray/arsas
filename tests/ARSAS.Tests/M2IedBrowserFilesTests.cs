using System.Xml.Linq;
using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;
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
            KnownSizeBytes = withCfg ? 9 : 5,
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
    public void ActualMmsDirectory_PreservesOrdinaryFilesAndFoldersWithoutFakeDownloads()
    {
        var index = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog(),
        [
            new MmsFileDirectoryEntry
            {
                Name = "config.xml", Path = "/config.xml", SizeBytes = 123
            },
            new MmsFileDirectoryEntry
            {
                Name = "records/", Path = "/records/", SizeBytes = 0
            }
        ]);
        Assert.Equal(2, index.Files.Count);
        Assert.Equal(123L, Assert.Single(index.Files,
            f => f.RemotePath == "/config.xml").SizeBytes);
        Assert.True(Assert.Single(index.Files,
            f => f.RemotePath == "/records/").IsDirectory);
        Assert.False(index.TryGetRecord("/config.xml", out _, out _));
        Assert.False(index.TryGetRecord("/records/", out _, out _));
    }

    [Fact]
    public void RootInventory_DoesNotDuplicateEngineVerifiedFaultRecords()
    {
        var record = Record();
        var index = new IedBrowserFileCatalog(
            new Iec61850FaultRecordCatalog { Records = [record] },
            [new MmsFileDirectoryEntry {
                Name = "FAULT001.cfg", Path = "/COMTRADE/FAULT001.cfg",
                SizeBytes = 4
            }]);
        Assert.Equal(2, index.Files.Count);
        Assert.True(index.TryGetRecord("/COMTRADE/FAULT001.cfg", out _, out _));
    }

    [Fact]
    public void EngineComtradeRecord_IsOneCompactDisplayRow_ForCompleteBundle()
    {
        var record = Record();
        var index = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog
        {
            Records = [record]
        });
        Assert.Equal(2, index.Files.Count); // raw provenance remains complete
        var display = Assert.Single(index.DisplayRows);
        Assert.Equal("FAULT001", display.Name);
        Assert.True(display.IsBundle);
        Assert.Equal(2, display.BundleMemberCount);
        Assert.Equal(9L, display.SizeBytes);
        Assert.Equal("/COMTRADE/FAULT001.cfg", display.RemotePath);
        Assert.True(index.TryGetRecord(display.RemotePath, out var exact, out _));
        Assert.Same(record, exact);
    }

    [Fact]
    public void UnclassifiedDirectoryAndFile_RemainBrowseOnlyWithoutFakeBundle()
    {
        var catalog = new IedBrowserFileCatalog(new Iec61850FaultRecordCatalog(),
        [
            new MmsFileDirectoryEntry { Name = "notes.txt", Path = "/notes.txt" },
            new MmsFileDirectoryEntry { Name = "DATA/", Path = "/DATA/" }
        ]);
        Assert.Equal(2, catalog.DisplayRows.Count);
        Assert.All(catalog.DisplayRows, row => Assert.False(row.IsBundle));
        Assert.False(catalog.TryGetRecord("/notes.txt", out _, out _));
    }

    [Fact]
    public void FilesTreeHasOnlyOneNavigationNode_NoDuplicatedFileChildren()
    {
        var files = IedBrowserPresentation.Roots(null, [],
            [new IedBrowserRemoteFile("a.cfg", "/a.cfg", 3, null)]);
        var root = Assert.Single(files, row => row.Key == "category/files");
        Assert.False(root.HasChildren);
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
    public void BrowseFilesToolbar_RevealsExistingFilesPageBeforeDirectoryRead()
    {
        var source = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        var start = source.IndexOf("private void IedBrowserBrowseFiles_Click",
            StringComparison.Ordinal);
        var end = source.IndexOf("private void IedBrowserRefresh_Click",
            start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = source[start..end];
        var navigate = handler.IndexOf("FindVisible(\"category/files\")",
            StringComparison.Ordinal);
        var read = handler.IndexOf("BrowseIedBrowserFilesAsync(device)",
            StringComparison.Ordinal);
        Assert.True(navigate >= 0 && read > navigate);
        Assert.Contains("RenderIedBrowserSelection(filesPage", handler,
            StringComparison.Ordinal);
        Assert.Contains("_iedBrowserSyncingSelection = true", handler,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeFilesGrid_ExposesThreeColumns_AndBoundedActions()
    {
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        var p = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var grid = Assert.Single(xaml.Descendants(p + "DataGrid"),
            e => e.Attribute(x + "Name")?.Value == "IedBrowserFilesGrid");
        var cols = grid.Element(p + "DataGrid.Columns")!
            .Elements().Select(c => (string?)c.Attribute("Header")).ToArray();
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
