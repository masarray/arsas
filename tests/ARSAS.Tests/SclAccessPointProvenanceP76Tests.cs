using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclAccessPointProvenanceP76Tests
{
    private const string Ied = "GR_X_7SX85";
    private static readonly string ShaA = new('a', 64);
    private static readonly string ShaB = new('b', 64);

    [Fact]
    public void Offline_SCD_AP_Choices_Exist_When_Both_8Mms_ConnectedAp_Have_No_Ip()
    {
        var document = new SclWorkspaceService().Parse(StationFixture(), "station.scd");
        var choices = SclEndpointTopology.Choices(document, Ied);

        Assert.Equal(2, choices.Count);
        Assert.Equal(new[] { "F", "J" }, choices.Select(item => item.AccessPointName).ToArray());
        Assert.All(choices, choice => Assert.False(choice.HasDeclaredAddress));
        Assert.Empty(SclEndpointTopology.Candidates(document, Ied));
        Assert.Single(SclEndpointTopology.FindExactWorkspace(document, choices[0])!.DesignModel.LogicalDevices);
        Assert.Single(SclEndpointTopology.FindExactWorkspace(document, choices[1])!.DesignModel.LogicalDevices);

        // A forged choice cannot borrow another AP or IED server or endpoint.
        Assert.Null(SclEndpointTopology.FindExactWorkspace(document,
            new SclAccessPointChoice("WRONG_IED", "J", null)));
        Assert.Null(SclEndpointTopology.FindExactWorkspace(document,
            new SclAccessPointChoice(Ied, "MISSING_AP", null)));
    }

    [Fact]
    public void Declared_And_Undeclared_Endpoints_Keep_Their_AP_Identity()
    {
        var oneDeclared = StationFixture().Replace(
            "<ConnectedAP iedName=\"GR_X_7SX85\" apName=\"J\" />",
            "<ConnectedAP iedName=\"GR_X_7SX85\" apName=\"J\"><Address><P type=\"IP\">192.0.2.11</P></Address></ConnectedAP>",
            StringComparison.Ordinal);
        var document = new SclWorkspaceService().Parse(oneDeclared, "station.scd");
        var choices = SclEndpointTopology.Choices(document, Ied);

        Assert.Equal(2, choices.Count);
        var j = choices.Single(item => item.AccessPointName == "J");
        var f = choices.Single(item => item.AccessPointName == "F");
        Assert.True(j.HasDeclaredAddress);
        Assert.Equal("192.0.2.11", j.DeclaredEndpoint!.IpAddress);
        Assert.False(f.HasDeclaredAddress);
        Assert.Null(f.DeclaredEndpoint);
        Assert.Equal("F", SclEndpointTopology.FindExactWorkspace(document, f)!.AccessPointName);
        Assert.Single(SclEndpointTopology.Candidates(document, Ied));
    }

    [Fact]
    public void Verified_Binding_Isolation_Uses_SourceSha_Ied_And_Ap_Not_Legacy_Ied_History()
    {
        WithTemporaryFile(path =>
        {
            SclEndpointBindingStore.RecordVerifiedAtPath(path, ShaA, Ied, "J", "192.0.2.11", 102);
            Assert.True(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "J", out var jIp, out var jPort));
            Assert.Equal("192.0.2.11", jIp);
            Assert.Equal(102, jPort);
            Assert.False(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "F", out _, out _));
            Assert.False(SclEndpointBindingStore.TryGetAtPath(path, ShaB, Ied, "J", out _, out _));
            Assert.False(SclEndpointBindingStore.TryGetAtPath(path, ShaA, "OTHER_IED", "J", out _, out _));

            SclEndpointBindingStore.RecordVerifiedAtPath(path, ShaA, Ied, "F", "198.51.100.12", 102);
            Assert.True(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "F", out var fIp, out _));
            Assert.Equal("198.51.100.12", fIp);
            Assert.True(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "J", out jIp, out _));
            Assert.Equal("192.0.2.11", jIp);
        });
    }

    [Fact]
    public void Multithread_Binding_Updates_Do_Not_Drop_Another_Aps_Last_Success()
    {
        WithTemporaryFile(path =>
        {
            Parallel.For(0, 32, iteration =>
            {
                var ap = iteration % 2 == 0 ? "J" : "F";
                var address = ap == "J" ? "192.0.2.11" : "198.51.100.12";
                SclEndpointBindingStore.RecordVerifiedAtPath(path, ShaA, Ied, ap, address, 102);
            });

            Assert.True(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "J", out var j, out _));
            Assert.True(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "F", out var f, out _));
            Assert.Equal("192.0.2.11", j);
            Assert.Equal("198.51.100.12", f);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(2, json.RootElement.GetArrayLength());
        });
    }

    [Fact]
    public void Corrupt_Or_Invalid_Endpoint_Binding_Is_Rejected_Fail_Closed()
    {
        WithTemporaryFile(path =>
        {
            File.WriteAllText(path, "{invalid JSON");
            Assert.False(SclEndpointBindingStore.TryGetAtPath(path, ShaA, Ied, "J", out _, out _));
            Assert.Throws<ArgumentException>(() =>
                SclEndpointBindingStore.RecordVerifiedAtPath(path, ShaA, Ied, "J", "0.0.0.0", 102));
            Assert.Throws<ArgumentException>(() =>
                SclEndpointBindingStore.RecordVerifiedAtPath(path, "not-a-sha", Ied, "J", "192.0.2.11", 102));
            Assert.False(SclEndpointBindingStore.TryGetAtPath(path, ShaB, Ied, "J", out _, out _));
        });
    }

    [Fact]
    public void Native_Wpf_Ap_Switch_Is_Offline_PerDevice_And_Never_Uses_Legacy_Ied_Fallback()
    {
        var chooser = ReadRepoFile("MainWindow.SclEndpointChoices.cs");
        var main = ReadRepoFile("MainWindow.xaml.cs");
        var diag = ReadRepoFile("Services/DiagnosticReportBuilder.cs");

        Assert.Contains("device.SclAccessPointChoices.Count < 2", chooser, StringComparison.Ordinal);
        Assert.Contains("device.IsBusy = true", chooser, StringComparison.Ordinal);
        Assert.Contains("device.IsBusy = false", chooser, StringComparison.Ordinal);
        Assert.Contains("document.SourceSha256.Equals(originalHash", chooser, StringComparison.Ordinal);
        Assert.Contains("RestoreKnownSclEndpointIfAvailable(device, workspace, allowLegacyIedHint: false)", chooser, StringComparison.Ordinal);
        Assert.Contains("var sameExactAp = device.SclSourceSha256.Equals(document.SourceSha256", main, StringComparison.Ordinal);
        Assert.Contains("SclEndpointBindingStore.RecordVerified(", main, StringComparison.Ordinal);
        Assert.Contains("SclEndpointOrigin", diag, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync(", chooser, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReconnect", chooser, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsRead", chooser, StringComparison.Ordinal);
    }

    private static void WithTemporaryFile(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "arsas-scl-ap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            action(Path.Combine(folder, "scl-successful-endpoints.json"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string ReadRepoFile(string relative)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var file = Path.Combine(current.FullName, relative);
            if (File.Exists(file)) return File.ReadAllText(file);
            current = current.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    private static string StationFixture() => """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="C">
          <Header id="SanitizedStation" />
          <Communication>
            <SubNetwork name="Station_J" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="J" />
            </SubNetwork>
            <SubNetwork name="Station_F" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="F" />
            </SubNetwork>
          </Communication>
          <IED name="GR_X_7SX85">
            <AccessPoint name="J">
              <Server><LDevice inst="Application">
                <LN0 lnClass="LLN0" lnType="LN0Type"/>
                <LN lnClass="XCBR" inst="1" lnType="XCBRType"/>
              </LDevice></Server>
            </AccessPoint>
            <AccessPoint name="F"><ServerAt apName="J"/></AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LN0Type" lnClass="LLN0"/>
            <LNodeType id="XCBRType" lnClass="XCBR"><DO name="Pos" type="PosType"/></LNodeType>
            <DOType id="PosType" cdc="DPC"><DA name="stVal" fc="ST" bType="Dbpos"/></DOType>
          </DataTypeTemplates>
        </SCL>
        """;
}
