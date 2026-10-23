using ArIED61850Tester.Services;

namespace ARSAS.Tests;
public sealed class SntpUserSettingsStoreTests
{
    [Fact]
    public void PersistsLastExplicitSelectionAndEnabledState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arsas-sntp-test", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir,"sntp.json");
            var expected = new SntpSavedSettings("nic-id","192.168.1.4",true);
            Assert.True(SntpUserSettingsStore.Save(expected,path));
            Assert.Equal(expected,SntpUserSettingsStore.Load(path));
            Assert.True(SntpUserSettingsStore.Save(expected with { Enabled = false },path));
            Assert.False(SntpUserSettingsStore.Load(path)!.Enabled);
        }
        finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }

    [Fact]
    public void CorruptOrUnsafePreferenceFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        Assert.Null(SntpUserSettingsStore.Load(path));
        try
        {
            File.WriteAllText(path,"{ invalid");
            Assert.Null(SntpUserSettingsStore.Load(path));
            Assert.False(SntpUserSettingsStore.Save(new("","192.168.1.4",true),path));
            Assert.False(SntpUserSettingsStore.Save(new("nic-id","127.0.0.1",true),path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void UiRestoreRequiresExactOriginalNicAndIpv4()
    {
        var source = Read("MainWindow.ClockSyncToggle.cs");
        Assert.Contains("SntpUserSettingsStore.Match(choices, _savedSntpSettings)",source);
        Assert.Contains("_savedSntpSettings is null && !_clockSyncEnabled",source);
        Assert.Contains("SntpUserSettingsStore.Save(saved)",source);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var full=Path.Combine(dir.FullName,path);
            if(File.Exists(full)) return File.ReadAllText(full);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
