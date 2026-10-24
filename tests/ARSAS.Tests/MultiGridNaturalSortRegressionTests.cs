using System.Xml.Linq;
using System.Collections;
using System.Windows.Controls;
using System.Windows.Data;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

[Collection("WpfStaIsolation")]
public sealed class MultiGridNaturalSortRegressionTests
{
    [Fact]
    public void ThreeRealGridsUseTheSharedViewOnlyNaturalSorter()
    {
        var xaml=Read("MainWindow.xaml");
        var xml=XDocument.Parse(xaml);
        XNamespace wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grids=xml.Descendants(wpf+"DataGrid").ToArray();
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="ExplorerLiveGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("SelectedDevice.Points")==true);
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="CommandGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("SelectedDevice.CommandSignals")==true);
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="GlobalLiveGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("GlobalPoints")==true);
        var sort=Read("MainWindow.IecNaturalSort.cs");
        Assert.Contains("view.CustomSort = comparer;",sort);
        Assert.Contains("IecNaturalCommandSort.Instance",sort);
        Assert.Contains("IecNaturalGlobalMonitorSort.Instance",sort);
    }

    [Fact]
    public void CommandDockSortingOrdersInstanceNumbersWithoutMutatingSource()
    {
        var one =new SignalDefinition { ObjectReference="BCUGEF650/CSWI1.Pos" };
        var two =new SignalDefinition { ObjectReference="BCUGEF650/CSWI2.Pos" };
        var ten =new SignalDefinition { ObjectReference="BCUGEF650/CSWI10.Pos" };
        var unordered =new[]{ten,two,one};
        var sorted=unordered.OrderBy(x=>x,
            Comparer<SignalDefinition>.Create((a,b)=>IecNaturalCommandSort.Instance.Compare(a,b)))
            .Select(x=>x.ObjectReference).ToArray();
        Assert.Equal(new[]{one.ObjectReference,two.ObjectReference,ten.ObjectReference},sorted);
        Assert.Same(ten,unordered[0]);
    }

    [Fact]
    public void ActualWpfCommandGridViewMustDisplayVerifiedSg7BreakerAheadOfGgioBank()
    {
        // Reproduces field screenshot: two verified control classes from
        // distinct logical devices. Data model sorting alone is insufficient,
        // because ListCollectionView.CustomSort is WPF's actual UI authority.
        RunInSta(() =>
        {
            static SignalDefinition Verified(string reference, string cdc = "DPC")
            {
                var signal = new SignalDefinition
                {
                    ObjectReference = reference,
                    Name = reference,
                    IsControlSignal = true,
                    IsSelected = true,
                    HasControlServiceEvidence = true,
                    FunctionalConstraint = "CO",
                    ControlCdc = cdc,
                    ControlModelText = "Select Before Operate (SBO) • Enhanced security"
                };
                signal.AcceptVerifiedLiveControlCdc(cdc);
                signal.ControlSemanticVerified = true;
                return signal;
            }

            // Start with a generic IO block, like the actual 04:28 field
            // trace where GGIO2.DPCSO01 is the first displayed command.
            var ggios = Enumerable.Range(1, 64)
                .Select(i => Verified($"SG7_BCUMaster/GGIO2.DPCSO{i:00}"))
                .ToArray();
            var breakers = Enumerable.Range(1, 4)
                .Select(i => Verified($"SG7_BCUSystem/Bkr0XCBR{i}.Pos"))
                .ToArray();
            var cswi = Enumerable.Range(1, 4)
                .Select(i => Verified($"SG7_BCUSystem/BkrCSWI{i}.Pos"))
                .ToArray();
            var switches = Enumerable.Range(1, 3)
                .Select(i => Verified($"SG7_BCUSystem/Disc0XSWI{i}.Pos"))
                .ToArray();
            var device = new Iec61850MonitorDevice
            {
                IsConnected = true,
                LiveDiscoveryModel = new LiveIedModelDiscoveryDocument()
            };
            device.Signals.ReplaceAll(ggios.Concat(cswi).Concat(switches).Concat(breakers));
            device.RecountSelectedSignals();

            // Mirror the production Dock: no WPF new-item placeholder in the view.
            var grid = new DataGrid
            {
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                ItemsSource = device.OperableCommandSignals
            };
            var view = Assert.IsType<ListCollectionView>(
                CollectionViewSource.GetDefaultView(grid.ItemsSource));
            view.SortDescriptions.Clear();
            view.CustomSort = IecNaturalCommandSort.Instance;
            SignalDefinition[] visual() => view.Cast<SignalDefinition>().ToArray();

            var first = visual();
            Assert.Equal(75, first.Length);
            // CSWI is the explicitly requested operator-first IEC switch
            // control tier; XCBR/XSWI remain discoverable below it.
            Assert.Equal(cswi.Select(s => s.ObjectReference),
                first.Take(4).Select(s => s.ObjectReference));
            Assert.Equal(breakers.Select(s => s.ObjectReference),
                first.Skip(4).Take(4).Select(s => s.ObjectReference));
            Assert.Equal(switches.Select(s => s.ObjectReference),
                first.Skip(8).Take(3).Select(s => s.ObjectReference));
            Assert.Equal(ggios.Select(s => s.ObjectReference),
                first.Skip(11).Select(s => s.ObjectReference));

            // Report refresh and WPF re-sort cannot promote GGIO back to top.
            var selected = breakers[0];
            selected.ControlInterlockCheck = false;
            for (var i = 0; i < 10; i++)
            {
                selected.ControlCurrentValue = i % 2 == 0 ? "Open" : "Closed";
                device.RefreshCommandSignalProjection();
                view.Refresh();
                Assert.Same(cswi[0], visual()[0]);
            }
            Assert.False(selected.ControlInterlockCheck);
        });
    }

    [Fact]
    public void RealWpfZivDockShowsLiveTapBeforeFourStatusOnlyCswi_WithoutSourceMutation()
    {
        RunInSta(() =>
        {
            static SignalDefinition Control(string reference, string cdc, string model, bool verified)
            {
                var signal = new SignalDefinition
                {
                    Name = reference,
                    ObjectReference = reference,
                    IsControlSignal = true,
                    IsSelected = true,
                    HasControlServiceEvidence = true,
                    FunctionalConstraint = "CO",
                    ControlCdc = cdc,
                    ControlModelText = model
                };
                if (verified)
                {
                    signal.AcceptVerifiedLiveControlCdc(cdc);
                    signal.ControlSemanticVerified = true;
                }
                return signal;
            }
            var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC",
                "Select Before Operate (SBO) • Normal security", true);
            var cswi = Enumerable.Range(1, 4)
                .Select(i => Control($"ZIVAVRLD1/CSWI{i}.Pos", "DPC", "Status only", false))
                .ToArray();
            var device = new Iec61850MonitorDevice();
            device.Signals.ReplaceAll([..cswi, tap]);
            device.RecountSelectedSignals();
            Assert.True(tap.ControlSupportsOperate);
            Assert.All(cswi, s => Assert.False(s.ControlSupportsOperate));
            Assert.Same(tap, device.CommandSignals[0]);

            // This is the actual WPF collection view that painted the
            // reported field screenshot incorrectly despite source order.
            var grid = new DataGrid
            {
                CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserSortColumns = false, ItemsSource = device.CommandSignals
            };
            var view = Assert.IsType<ListCollectionView>(
                CollectionViewSource.GetDefaultView(grid.ItemsSource));
            view.SortDescriptions.Clear();
            view.CustomSort = IecNaturalCommandSort.Instance;
            SignalDefinition[] Visual() => view.Cast<SignalDefinition>().ToArray();

            Assert.Same(tap, Visual()[0]);
            Assert.Equal(cswi, Visual().Skip(1));
            Assert.Equal(5, Visual().Length);

            // Report/value churn must not recycle rows or elevate read-only
            // controls. Changing process values never changes ctlModel.
            var changes = 0;
            device.CommandSignals.CollectionChanged += (_, _) => changes++;
            for (var i = 0; i < 100; ++i)
            {
                tap.ControlCurrentValue = (i % 2 + 1).ToString();
                device.RefreshCommandSignalProjection();
            }
            Assert.Equal(0, changes);
            Assert.Same(tap, Visual()[0]);
            Assert.All(Visual().Skip(1), s => Assert.False(s.ControlSupportsOperate));
        });
    }

    [Fact]
    public void CommandGridForbidsHeaderSortingAndResetsViewportOnIedSwitch()
    {
        var xaml = Read("MainWindow.xaml");
        var xml = XDocument.Parse(xaml);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var command = xml.Descendants(wpf + "DataGrid")
            .Single(g => (string?)g.Attribute(x + "Name") == "EquipmentCommandGrid");
        Assert.Equal("False", (string?)command.Attribute("CanUserSortColumns"));

        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("ScrollVerifiedEquipmentCommandsIntoView();", code);
        Assert.Contains("scroll.ScrollToTop();", code);
    }

    [Fact]
    public void CommandComparatorDoesNotTreatUnverifiedNamesAsOperatePermission()
    {
        var gg = new SignalDefinition { ObjectReference = "SG7_BCUMaster/GGIO2.DPCSO01",
            ControlCdc = "DPC", IsControlSignal = true };
        var cb = new SignalDefinition { ObjectReference = "SG7_BCUSystem/Bkr0XCBR1.Pos",
            ControlCdc = "DPC", IsControlSignal = true };
        Assert.True(IecNaturalCommandSort.Instance.Compare(cb, gg) < 0);
        Assert.False(cb.ControlSupportsOperate);
        Assert.False(gg.ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets([cb, gg], true));
    }

    [Fact]
    public void GlobalMonitorGroupsIEDThenNaturalInstance()
    {
        static Iec61850MonitorPoint Point(string device,string ln) =>
            new(){DeviceName=device,IecReference=$"{device}F650/{ln}.Pos.stVal"};
        var points=new[]{Point("GE","CSWI10"),Point("GE","CSWI2"),Point("ABB","CSWI10"),
            Point("GE","CSWI1"),Point("ABB","CSWI2")};
        var sorted=points.OrderBy(x=>x,
            Comparer<Iec61850MonitorPoint>.Create((a,b)=>IecNaturalGlobalMonitorSort.Instance.Compare(a,b))).ToArray();
        Assert.Equal(new[]{"ABB","ABB","GE","GE","GE"},sorted.Select(x=>x.DeviceName));
        Assert.Equal(new[]{"CSWI2","CSWI10"},sorted.Take(2).Select(x=>x.IecTelegram.Split('/').Last().Split('.')[0]));
        Assert.Equal(new[]{"CSWI1","CSWI2","CSWI10"},sorted.Skip(2).Select(x=>x.IecTelegram.Split('/').Last().Split('.')[0]));
    }

    private static void RunInSta(Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { thrown = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF command-view sort test timed out");
        if (thrown is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown).Throw();
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir=new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var file=Path.Combine(dir.FullName,path);
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
