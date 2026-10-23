using System.Collections.Specialized;
using System.Xml.Linq;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// A WPF recycled Command Dock row must have its own declarative ctlModel
/// gear+label; a best-effort Loaded-time material decorator cannot own the icon.
/// The selected control catalog is IEC DataObject based, never IED-vendor based.
/// </summary>
public sealed class P109ABCtlModelGlyphAndVendorNeutralCatalogTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(file);
    }

    [Fact]
    public void EveryVirtualizedRowGetsAStaticSingleOwnerSettingsGlyphAndCtlModelLabel()
    {
        var window = XDocument.Parse(Source("MainWindow.xaml"));
        var mainGrid = window.Descendants(P + "DataGrid").Single(g =>
            (string?)g.Attribute("ItemsSource") == "{Binding SelectedDevice.OperableCommandSignals}");
        Assert.Equal("Recycling", (string?)mainGrid.Attribute("VirtualizingPanel.VirtualizationMode"));
        var modelButtons = mainGrid.Descendants(P + "Button").Where(b =>
            (string?)b.Attribute("Click") == "ControlDetails_Click").ToArray();
        var button = Assert.Single(modelButtons);
        Assert.Equal("ctlModel", (string?)button.Attribute("Content"));
        Assert.Equal("{StaticResource CommandDockCtlModelButton}", (string?)button.Attribute("Style"));
        Assert.Equal("{Binding}", (string?)button.Attribute("Tag"));
        Assert.Contains("SelectedDevice.IsConnected",
            (string?)button.Attribute("IsEnabled"), StringComparison.Ordinal);
        Assert.Null(button.Attribute("ContentTemplate")); // owned by the immutable style
        Assert.Empty(button.Elements(P + "Button.Content")); // no added visual child per row

        var app = XDocument.Parse(Source("App.xaml"));
        var styles = app.Descendants(P + "Style").ToArray();
        var modelStyle = Assert.Single(styles.Where(s =>
            (string?)s.Attribute(X + "Key") == "CommandDockCtlModelButton"));
        Assert.Equal("{StaticResource CommandDockSoftButton}", (string?)modelStyle.Attribute("BasedOn"));
        var templateSetter = Assert.Single(modelStyle.Elements(P + "Setter").Where(s =>
            (string?)s.Attribute("Property") == "ContentTemplate"));
        Assert.Equal("{StaticResource MaterialSettingsAction}",
            (string?)templateSetter.Attribute("Value"));
        Assert.Contains(modelStyle.Elements(P + "Setter"), s =>
            (string?)s.Attribute("Property") == "MinWidth" &&
            (string?)s.Attribute("Value") == "88");
        var template = Assert.Single(app.Descendants(P + "DataTemplate").Where(t =>
            (string?)t.Attribute(X + "Key") == "MaterialSettingsAction"));
        var icon = Assert.Single(template.Descendants(P + "Path"));
        Assert.Equal("{StaticResource MaterialSettingsIcon}", (string?)icon.Attribute("Data"));
        Assert.Contains("AncestorType", (string?)icon.Attribute("Fill"), StringComparison.Ordinal);
        Assert.Equal("{Binding}", (string?)Assert.Single(template.Descendants(P + "TextBlock")).Attribute("Text"));
        Assert.Single(app.Descendants(P + "Geometry").Where(g =>
            (string?)g.Attribute(X + "Key") == "MaterialSettingsIcon"));

        // The historical Loaded/layout pass only recognizes the old "Model"
        // caption. The compiled ctlModel button no longer depends on its
        // window-wide visual-tree scan and can survive WPF row recycling.
        var decorator = Source("P2IndustrialWorkstationUx.cs");
        Assert.Contains("else if (text is \"Model\")", decorator, StringComparison.Ordinal);
        Assert.DoesNotContain("else if (text is \"ctlModel\")", decorator, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource CommandDockCtlModelButton}\"", Source("MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void HundredsOfUnchangedControlsNeedNoResetMoveOrReplaceNotifications()
    {
        var device = new Iec61850MonitorDevice();
        var selected = Enumerable.Range(1, 240).Select(i => new SignalDefinition
        {
            Name = "Pos",
            ObjectReference = $"LD0/CSWI{i}.Pos",
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            IsSelected = true,
            ControlCdc = "DPC",
            ControlModelText = "Status only"
        }).ToArray();
        device.Signals.ReplaceAll(selected);
        device.RecountSelectedSignals();
        Assert.Equal(selected.Length, device.CommandSignals.Count);
        var frozen = device.CommandSignals.ToArray();
        var notifications = new List<NotifyCollectionChangedAction>();
        device.CommandSignals.CollectionChanged += (_, e) => notifications.Add(e.Action);

        // No IED I/O. Repeated model verification and tab revisits must be
        // an identity-only fast path with stable WPF container lifetime.
        for (var attempt = 0; attempt < 40; attempt++)
            device.RefreshCommandSignalProjection();
        Assert.Empty(notifications);
        Assert.True(frozen.SequenceEqual(device.CommandSignals, ReferenceEqualityComparer.Instance));

        selected[3].IsSelected = false;
        device.RecountSelectedSignals();
        Assert.DoesNotContain(selected[3], device.CommandSignals);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, notifications);
        Assert.Equal(239, device.CommandSignals.Count);
    }

    [Fact]
    public void VendorIndependentSelectedRootsRemainStableWhileWritePermissionChanges()
    {
        var devices = new[]
        {
            new Iec61850MonitorDevice { Name = "ZIVAVR" },
            new Iec61850MonitorDevice { Name = "SIEBCU" },
            new Iec61850MonitorDevice { Name = "GE_C264" }
        };
        var references = new[]
        {
            "ZIVAVRLD1/CSWI1.Pos",
            "SIEBCUCTRL/CSWI1.Pos",
            "C264CONTROL/CSWI1.Pos"
        };

        for (var i = 0; i < devices.Length; i++)
        {
            var row = new SignalDefinition
            {
                ObjectReference = references[i],
                Name = "Pos",
                FunctionalConstraint = "CO",
                IsControlSignal = true,
                IsSelected = true,
                ControlCdc = "DPC",
                ControlModelText = "Status only"
            };
            var decoyStatus = new SignalDefinition
            {
                ObjectReference = references[i] + ".stVal",
                FunctionalConstraint = "ST",
                IsSelected = true,
                IsControlSignal = false
            };
            devices[i].Signals.ReplaceAll([row, decoyStatus]);
            devices[i].RecountSelectedSignals();
            Assert.Same(row, Assert.Single(devices[i].CommandSignals));
            Assert.False(row.ControlSupportsOperate);
            Assert.Empty(Iec61850ControlIdentity.DistinctOperable(devices[i].Signals));

            var changes = new List<NotifyCollectionChangedAction>();
            devices[i].CommandSignals.CollectionChanged += (_, e) => changes.Add(e.Action);
            // A new ctlModel interpretation is not a change in DataObject
            // identity, so neither virtualization nor checkbox state resets.
            row.ControlModelText = "SBO • Enhanced security";
            for (var j = 0; j < 50; j++) devices[i].RefreshCommandSignalProjection();
            Assert.Empty(changes);
            Assert.Same(row, Assert.Single(devices[i].CommandSignals));
            Assert.False(row.ControlSupportsOperate); // typed proof still absent
        }
    }
}
