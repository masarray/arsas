using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217NNameplateAndExplorerGeometryTests
{
    private const string Owner = "BCUGEF650/MMXU1.NamPlt";

    private static LiveIedDataAttributeModel Attr(string leaf, string fc = "DC",
        string? owner = null) => new()
    {
        ObjectReference = owner ?? Owner,
        AttributePath = "NamPlt." + leaf,
        FunctionalConstraint = fc,
        MmsReference = (owner ?? Owner).Replace(".NamPlt", "") +
            "$" + fc + "$NamPlt$" + leaf,
        SclBType = "VisString255"
    };

    private static LiveIedDataObjectModel Nameplate(params LiveIedDataAttributeModel[] attrs)
        => new() { Name = "NamPlt", Reference = Owner, Attributes = attrs };

    [Fact]
    public void ExpandedObservedDcVendorAlsoPopulatesNameplateDoWithoutAnotherRead()
    {
        var obj = Nameplate(Attr("vendor"), Attr("swRev"), Attr("d"),
            Attr("vendor", "ST"),
            Attr("vendor", owner: "BCUGEF650/MMXU2.NamPlt"));
        var row = IedBrowserStructuredDetails.DataObject(obj, uniqueProcessFc: null);
        Assert.Equal("NamPlt", row.Name);
        Assert.Equal(3, row.NameplateDcReferences.Count);
        Assert.Contains(Owner + ".vendor", row.NameplateDcReferences);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".vendor", "DC", "GE Multilin", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".swRev", "DC", "6.20", "", "", "MMS Read"));
        var evidence = new IedBrowserCfEvidencePresentation(ledger, null, null);
        Assert.True(IedBrowserNameplatePresentation.TryProject(row,
            ledger, evidence, out var presentation));
        Assert.Equal("GE Multilin", presentation.Value);
        Assert.Equal("MMS Read", presentation.Source);
        Assert.Contains(".vendor [DC]", presentation.Provenance);
        // Nameplate summary is a projection. No invented parent scalar.
        Assert.False(ledger.TryGet(Owner, "DC", out _));
        Assert.False(ledger.TryGet(Owner + ".vendor", "ST", out _));
    }

    [Fact]
    public void NameplateUsesOnlyExactDcLeavesAndReconnectNeverShowsOldLiveValue()
    {
        var row = IedBrowserStructuredDetails.DataObject(Nameplate(
            Attr("vendor"), Attr("d"), Attr("swRev")),
            uniqueProcessFc: null);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".vendor", "ST", "BAD", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".d", "DC", "Measurement", "", "", "MMS Read"));
        var evidence = new IedBrowserCfEvidencePresentation(ledger, null, null);
        Assert.True(IedBrowserNameplatePresentation.TryProject(row, ledger, evidence,
            out var result));
        Assert.Equal("Measurement", result.Value);
        ledger.BeginAssociation();
        Assert.False(IedBrowserNameplatePresentation.TryProject(row,
            ledger, evidence, out _));
    }

    [Fact]
    public void EmptyLegacyDockRowCannotStealExplorerHeightAfterReparent()
    {
        foreach (var controls in new[] { 0, 5, 6, 16, 150 })
        {
            var detached = ExplorerCommandDockLayoutPolicy.LegacyRowHeight(
                dockReparented: true, commandExpanded: true, controlCount: controls);
            Assert.True(detached.IsAuto);
        }
        Assert.True(ExplorerCommandDockLayoutPolicy.LegacyRowHeight(
            dockReparented: false, commandExpanded: false, controlCount: 16).IsAuto);
        Assert.True(ExplorerCommandDockLayoutPolicy.LegacyRowHeight(
            dockReparented: false, commandExpanded: true, controlCount: 5).IsAuto);
        Assert.True(ExplorerCommandDockLayoutPolicy.LegacyRowHeight(
            dockReparented: false, commandExpanded: true, controlCount: 6).IsStar);
    }

    [Fact]
    public void RuntimeDockTransferResetsEmptyLegacyRowAndKeepsExplorerGridStar()
    {
        static string Source(string relative)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root != null)
            {
                var filename = Path.Combine(root.FullName, relative);
                if (File.Exists(filename)) return File.ReadAllText(filename);
                root = root.Parent;
            }
            throw new FileNotFoundException(relative);
        }
        var transfer = Source("MainWindow.PersistentWorkbench.cs");
        Assert.Contains("ExplorerCommandDockRow.Height = GridLength.Auto;", transfer);
        var ui = Source("MainWindow.xaml.cs");
        Assert.Contains("dockReparented: _persistentWorkbench is not null", ui);
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("<RowDefinition Height=\"*\"/>", xaml);
        Assert.Contains("Grid.Row=\"2\" ItemsSource=\"{Binding SelectedDevice.Points}\"", xaml);
    }
}
