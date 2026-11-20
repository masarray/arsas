using System.Net;
using System.Xml.Linq;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Rc0GooseSntpBrowserFieldUxTests
{
    [Fact]
    public void SntpPicker_AlwaysRendersIpv4AddressInsteadOfRecordType()
    {
        var address = IPAddress.Parse("192.168.1.175");
        var binding = new SntpNetworkBinding(address, IPAddress.Parse("255.255.255.0"),
            null, "Ethernet", "GUID");
        Assert.Equal("192.168.1.175", binding.ToString());
        var header = Read("MainWindow.ClockSyncToggle.cs");
        Assert.Contains("DisplayMemberPath = nameof(SntpNetworkBinding.LocalAddress)", header);
    }

    [Fact]
    public void RcbAndGooseLastReceivedSections_DefaultCollapsedButRespectRestoredUserChoice()
    {
        var source = new IedBrowserDetailRow[]
        {
            new("Section", "Control Block attributes"),
            new("Property", "Enabled", "Not read"),
            new("Section", "Information received in last Report"),
            new("Property", "Received frame count", "0"),
            new("Section", "Data"),
            new("DataHeader", "Name", "Value")
        };
        var projection = new IedBrowserDetailProjection();
        projection.Reset(source);
        var defaults = projection.Rows.Where(r => r.Kind == "Section").ToArray();
        Assert.True(defaults.Single(r => r.Name == "Control Block attributes").IsExpanded);
        Assert.False(defaults.Single(r => r.Name == "Information received in last Report").IsExpanded);
        projection.Reset(source, new HashSet<string>(StringComparer.Ordinal)
        {
            "Information received in last Report"
        });
        var restored = projection.Rows.Where(r => r.Kind == "Section").ToArray();
        Assert.False(restored.Single(r => r.Name == "Control Block attributes").IsExpanded);
        Assert.True(restored.Single(r => r.Name == "Information received in last Report").IsExpanded);

        var goose = IedBrowserDetailSectionLayout.Attach(
            new IedBrowserDetailRow[]
            {
                new("Section", "Information received in last GOOSE"),
                new("Property", "Frame received at", "—"),
                new("Section", "Data")
            });
        Assert.False(goose.Single(r => r.Name == "Information received in last GOOSE").ExpandByDefault);
    }

    [Fact]
    public void GooseOperatorScope_StartsAllPublisherAndNeverForgesUnresolvedOwnership()
    {
        Assert.True(GooseIedScopePolicy.Matches(1, "SG7_BCU", "SG7_BCU", "Unresolved"));
        Assert.False(GooseIedScopePolicy.Matches(0, "SG7_BCU", "SG7_BCU", "Unresolved"));
        var source = Read("MainWindow.GooseIedScope.cs");
        Assert.Contains("private int _gooseIedScopeIndex = 1;", source);
        Assert.Contains("none has a verified publisher identity", source);
        Assert.Contains("_gooseEventScopeView?.Refresh()", source);
        var ingress = Read("MainWindow.GooseTimeline.cs");
        Assert.Contains("MaxPendingGooseTimelineEvents = 512", ingress);
        Assert.Contains("MaxGooseTimelineEvents = 500", ingress);
        Assert.Contains("while (processed < 48", ingress);
    }

    [Fact]
    public void GooseTimeline_GrowsIntoAvailableViewportAndHasIndependentDockPreference()
    {
        var view = XDocument.Parse(Read("Views/GooseSubscriberLiteView.xaml"));
        Assert.Equal("0", (string?)view.Root?.Attribute("MinHeight"));
        var shell = Read("MainWindow.PersistentWorkbench.cs");
        Assert.Contains("GooseDockExpanded { get; set; } = false", shell);
        Assert.Contains("workspaceIndex == GooseSubscriberTabIndex", shell);
        Assert.Contains("state.GooseDockExpanded = expanded", shell);
        Assert.Contains("state.GooseDockExpanded", shell);
        Assert.Contains("GooseEmptyEventDetail", Read("Views/GooseSubscriberLiteView.xaml"));
    }

    [Fact]
    public void BrowserRetainsPerDeviceAndPerPageExpansionNotRemoteMmsObservations()
    {
        var src = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("_iedBrowserTreePreferences", src);
        Assert.Contains("_iedBrowserSectionPreferences", src);
        Assert.Contains("RememberIedBrowserTreePreferences();", src);
        Assert.Contains("RememberIedBrowserSectionPreferences();", src);
        Assert.Contains("BrowserSectionKey(SelectedDevice, _iedBrowserRenderedPage)", src);
        Assert.Contains("_iedBrowserDetails.Reset(rows, restored)", src);
        Assert.Contains("recalled.ExpandedKeys", src);
        Assert.DoesNotContain("BuildRcbExportRows(device, inventory, _iedBrowserSnapshot);\n            _iedBrowserSectionPreferences", src);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
