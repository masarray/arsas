using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclServerAtEndpointSelectionTests
{
    [Fact]
    public void SiemensStyle_ServerAt_Resolves_Both_Scd_Addresses_And_One_Model_Per_Ap()
    {
        var document = new SclWorkspaceService().Parse(SanitizedStation(), "station.scd");
        var candidates = SclEndpointTopology.Candidates(document, "GR_X_7SX85");

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, x => x.AccessPointName == "J" && x.IpAddress == "192.0.2.11");
        Assert.Contains(candidates, x => x.AccessPointName == "F" && x.IpAddress == "198.51.100.12");
        Assert.All(candidates, endpoint =>
        {
            var workspace = SclEndpointTopology.FindExactWorkspace(document, endpoint);
            Assert.NotNull(workspace);
            Assert.True(workspace.CanBrowseOffline);
            Assert.Equal(endpoint.AccessPointName, workspace.AccessPointName);
            Assert.Equal(endpoint.IpAddress, workspace.PreferredEndpoint!.IpAddress);
            Assert.Single(workspace.DesignModel.LogicalDevices);
        });
    }

    [Fact]
    public void Endpoint_Selection_Is_Exact_And_Never_Borrows_Another_Ied_Or_Ip()
    {
        var document = new SclWorkspaceService().Parse(SanitizedStation(), "station.scd");
        var declared = SclEndpointTopology.Candidates(document, "GR_X_7SX85");
        Assert.Empty(SclEndpointTopology.Candidates(document, "WRONG_IED"));

        var f = declared.Single(x => x.AccessPointName == "F");
        var otherAddress = new SclMmsEndpoint
        {
            IedName = f.IedName, AccessPointName = f.AccessPointName,
            IpAddress = "203.0.113.99", Port = 102, IsValidIpAddress = true
        };
        Assert.Null(SclEndpointTopology.FindExactWorkspace(document, otherAddress));

        var otherIed = new SclMmsEndpoint
        {
            IedName = "WRONG_IED", AccessPointName = f.AccessPointName,
            IpAddress = f.IpAddress, Port = f.Port, IsValidIpAddress = true
        };
        Assert.Null(SclEndpointTopology.FindExactWorkspace(document, otherIed));
    }

    [Fact]
    public void Native_Ied_Card_Exposes_Offline_Ap_Switch_Without_Automatic_Connect()
    {
        var xaml = ReadRepoFile("MainWindow.xaml");
        var chooser = ReadRepoFile("MainWindow.SclEndpointChoices.cs");
        var owner = ReadRepoFile("MainWindow.xaml.cs");

        Assert.Contains("SclEndpoint_ContextMenuOpening", xaml, StringComparison.Ordinal);
        Assert.Contains("SclEndpointHint", xaml, StringComparison.Ordinal);
        Assert.Contains("SclEndpointTopology.Candidates(document, workspace.IedName)", owner, StringComparison.Ordinal);
        Assert.Contains("SclEndpointTopology.FindExactWorkspace(document, selected)", chooser, StringComparison.Ordinal);
        Assert.Contains("document.SourceSha256.Equals(originalHash", chooser, StringComparison.Ordinal);
        Assert.Contains("device.IsConnected || device.IsBusy || device.IsMonitoring", chooser, StringComparison.Ordinal);
        Assert.Contains("RemoveDevicePoints(device.DeviceId)", chooser, StringComparison.Ordinal);
        Assert.Contains("device.Points.Clear()", chooser, StringComparison.Ordinal);
        Assert.Contains("device.LiveDiscoveryModel = null", chooser, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync(", chooser, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReconnect", chooser, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(", chooser, StringComparison.Ordinal);
    }

    private static string SanitizedStation() => """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="C">
          <Header id="SanitizedStation" />
          <Communication>
            <SubNetwork name="J" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="J">
                <Address><P type="IP">192.0.2.11</P></Address>
              </ConnectedAP>
            </SubNetwork>
            <SubNetwork name="F" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="F">
                <Address><P type="IP">198.51.100.12</P></Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
          <IED name="GR_X_7SX85">
            <AccessPoint name="J">
              <Server>
                <LDevice inst="Application">
                  <LN0 lnClass="LLN0" lnType="LN0Type" />
                  <LN lnClass="XCBR" inst="1" lnType="XCBRType"/>
                </LDevice>
              </Server>
            </AccessPoint>
            <AccessPoint name="F"><ServerAt apName="J"/></AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LN0Type" lnClass="LLN0"/>
            <LNodeType id="XCBRType" lnClass="XCBR">
              <DO name="Pos" type="PosType"/>
            </LNodeType>
            <DOType id="PosType" cdc="DPC">
              <DA name="stVal" fc="ST" bType="Dbpos"/>
            </DOType>
          </DataTypeTemplates>
        </SCL>
        """;

    private static string ReadRepoFile(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var file = Path.Combine(directory.FullName, relative);
            if (File.Exists(file))
                return File.ReadAllText(file);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
