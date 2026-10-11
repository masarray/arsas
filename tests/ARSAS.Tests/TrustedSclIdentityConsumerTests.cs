using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using System.Security.Cryptography;

namespace ARSAS.Tests;

public sealed class TrustedSclIdentityConsumerTests
{
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
