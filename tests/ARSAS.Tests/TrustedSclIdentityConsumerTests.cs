using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class TrustedSclIdentityConsumerTests
{

    [Fact]
    public void Previous_Live_Model_Heuristic_Cannot_Override_A_Fresh_Five_Domain_Identity()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.55",
            IedName = "SIE",
            LogicalDevices = new[] { "SIEBCUCTRL", "SIEBCUMEAS", "SIEBCUPROT", "SIEBCUUD1", "SIEBCUUD2" }
                .Select(domain => new LiveIedLogicalDeviceModel { MmsDomain = domain, Inst = domain })
                .ToArray()
        };
        var resolved = Iec61850DeviceIdentityResolver.Resolve(
            null, model, Array.Empty<SignalDefinition>());

        Assert.Equal("SIEBCU", resolved.IedName);
        Assert.Contains("MmsDomainCommonPrefix (Medium)", resolved.Source, StringComparison.Ordinal);
        Assert.Equal("UD2", resolved.LogicalDevices.Single(ld => ld.Domain == "SIEBCUUD2").Instance);
    }

    [Fact]
    public async Task Wrong_Explicit_AccessPoint_Does_Not_Authenticate_Trusted_Scl_Identity()
    {
        using var temp = TempScl.Create("SIEBCU", "CTRL", "192.0.2.55");
        temp.Device.SclAccessPointName = "DifferentAP";

        Assert.Null(await TrustedSclIdentityAuthority.TryMatchAsync(
            temp.Device, Model("SIEBCUCTRL")));
    }

    [Fact]
    public async Task Sha_Pinned_Five_Domain_Scl_Promotes_Exact_Ied_And_Every_Ld()
    {
        using var temp = TempScl.Create("SIEBCU", "CTRL", "192.0.2.55");
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var document = XDocument.Load(temp.Device.SclSourcePath);
        var server = Assert.Single(document.Descendants(ns + "Server"));
        foreach (var inst in new[] { "MEAS", "PROT", "UD1", "UD2" })
            server.Add(new XElement(ns + "LDevice", new XAttribute("inst", inst)));
        document.Save(temp.Device.SclSourcePath);
        temp.Device.SclSourceSha256 = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(temp.Device.SclSourcePath)));

        var domains = new[] { "SIEBCUCTRL", "SIEBCUMEAS", "SIEBCUPROT", "SIEBCUUD1", "SIEBCUUD2" };
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.55",
            IedName = "SIE",
            LogicalDevices = domains.Select(domain =>
                new LiveIedLogicalDeviceModel { MmsDomain = domain, Inst = domain }).ToArray()
        };
        var identity = await TrustedSclIdentityAuthority.TryMatchAsync(temp.Device, model);
        Assert.NotNull(identity);
        var projected = TrustedSclIdentityAuthority.ToConsumerIdentity(identity, domains);

        Assert.Equal("SIEBCU", projected.IedName);
        Assert.Equal(5, projected.LogicalDevices.Count);
        Assert.Equal("UD2", projected.LogicalDevices.Single(ld => ld.Domain == "SIEBCUUD2").Instance);
    }

    [Fact]
    public async Task GE_F650_SHA_Pinned_Engineering_Source_Promotes_IED_And_LD_Without_Touching_MMS_Domain()
    {
        using var temp=TempScl.Create("BCUGE","F650","192.16.1.33");
        var device=temp.Device;
        var model=Model("BCUGEF650");
        var identity=await TrustedSclIdentityAuthority.TryMatchAsync(device,model);
        Assert.NotNull(identity);
        Assert.Equal("BCUGE",identity.IedName);
        Assert.Equal("F650",identity.LogicalDeviceAliases["BCUGEF650"]);
        var projected=TrustedSclIdentityAuthority.ToConsumerIdentity(identity,["BCUGEF650"]);
        Assert.Equal("BCUGE",projected.IedName);
        Assert.Equal("BCUGEF650",Assert.Single(projected.LogicalDevices).Domain);
        Assert.Equal("F650",Assert.Single(projected.LogicalDevices).Instance);
    }

    [Fact]
    public async Task Changed_SCL_SHA_Must_Fail_Closed_And_Never_Rename()
    {
        using var temp=TempScl.Create("BCUGE","F650","192.16.1.33");
        var device=temp.Device;
        device.SclSourceSha256=new string('0',64);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TrustedSclIdentityAuthority.TryMatchAsync(device,Model("BCUGEF650")));
    }

    [Fact]
    public async Task Missing_Trusted_SCL_And_Wrong_MMS_Endpoint_Does_Not_Guess()
    {
        var plain=new Iec61850MonitorDevice{IpAddress="192.16.1.33"};
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchAsync(plain,Model("BCUGEF650")));
        using var temp=TempScl.Create("BCUGE","F650","192.16.1.33");
        temp.Device.IpAddress="192.16.1.34";
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchAsync(
            temp.Device,Model("BCUGEF650")));
    }

    [Fact]
    public void Canonical_Save_And_RCB_Export_Must_Consume_Verified_Identity()
    {
        var save=Read("MainWindow.xaml.cs");
        Assert.Contains("verifiedIdentity: verifiedIdentity",save);
        Assert.Contains("verifiedIdentity?.IedName",save);
        Assert.Contains("TrustedSclIdentityAuthority.TryMatchAsync",save);
        var validator=Read("Services/CanonicalSclReloadValidator.cs");
        Assert.Contains("effectiveIedName",validator);
        var export=Read("MainWindow.RcbExport.cs");
        Assert.Contains("VerifiedIdentity = verifiedIdentity",export);
        Assert.Contains("IedNameOverride = verifiedIdentity?.IedName ?? device.Name",export);
        var runtime=Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("TrustedSclIdentityAuthority.ToConsumerIdentity",runtime);
    }

    private static LiveIedModelDiscoveryDocument Model(string domain) => new()
    {
        Host="192.16.1.33",IedName="IED_192_16_1_33",
        LogicalDevices=[new LiveIedLogicalDeviceModel{MmsDomain=domain,Inst=domain}]
    };

    private sealed class TempScl : IDisposable
    {
        private readonly string _folder;
        public Iec61850MonitorDevice Device { get; }
        private TempScl(string folder,Iec61850MonitorDevice device)
        {
            _folder=folder;Device=device;
        }
        public static TempScl Create(string ied,string ld,string ip)
        {
            var dir=Path.Combine(Path.GetTempPath(),"arsas-ied-p10-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,"engineering.iid");
            var xml=$"""
                <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
                  <Communication><SubNetwork name="Bus"><ConnectedAP iedName="{ied}" apName="S1"><Address><P type="IP">{ip}</P></Address></ConnectedAP></SubNetwork></Communication>
                  <IED name="{ied}"><AccessPoint name="S1"><Server><LDevice inst="{ld}"><LN0 lnClass="LLN0" lnType="LLN0_t"/></LDevice></Server></AccessPoint></IED>
                </SCL>
                """;
            File.WriteAllText(path,xml);
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            return new TempScl(dir,new Iec61850MonitorDevice
            {
                IpAddress=ip,
                SclSourcePath=path,
                SclSourceSha256=hash,
                SclIedName=ied,
                SclAccessPointName="S1"
            });
        }
        public void Dispose()=>Directory.Delete(_folder,recursive:true);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir=new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var file=Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
