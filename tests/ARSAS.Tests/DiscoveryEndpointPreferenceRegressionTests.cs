using System.Text.Json;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Test the actual persistence code using isolated temporary files, not the
/// operator's AppData. No MMS sockets, report controls, or process polling.
/// </summary>
public sealed class DiscoveryEndpointPreferenceRegressionTests
{
    [Fact]
    public void LastSubmittedIpAndCustomPort_SurviveReload_AndPreserveExistingProfiles()
    {
        WithTemporaryPreferences(path =>
        {
            File.WriteAllText(path, """
                {"SuccessfulRelays":[{"IpAddress":"10.0.0.44","MmsPort":102,"IedName":"OLD"}],
                 "SignalSelectionProfiles":[{"IedName":"IED1","IpAddress":"10.0.0.44",
                   "Port":102,"SelectedObjectReferences":["LD0/LLN0.Mod.stVal"]}],
                 "RecentRelayIps":["10.0.0.50"]}
                """);

            UserPreferenceStore.RecordLastDiscoveryEndpoint(path, " 192.16.1.33 ", 1102);

            Assert.True(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(
                path, out var storedIp, out var storedPort));
            Assert.Equal("192.16.1.33", storedIp);
            Assert.Equal(1102, storedPort);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var prefs = document.RootElement;
            Assert.Equal("OLD", prefs.GetProperty("SuccessfulRelays")[0].GetProperty("IedName").GetString());
            Assert.Equal("LD0/LLN0.Mod.stVal",
                prefs.GetProperty("SignalSelectionProfiles")[0]
                    .GetProperty("SelectedObjectReferences")[0].GetString());
            Assert.Equal("10.0.0.50", prefs.GetProperty("RecentRelayIps")[0].GetString());
        });
    }

    [Fact]
    public void SecondValidSubmissionWins_EvenIfNoSuccessfulConnectionWasRecorded()
    {
        WithTemporaryPreferences(path =>
        {
            Assert.False(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(path, out _, out _));
            UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "192.168.81.17", 102);
            UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "192.16.1.33", 102);

            Assert.True(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(
                path, out var ip, out var port));
            Assert.Equal("192.16.1.33", ip);
            Assert.Equal(102, port);
        });
    }

    [Fact]
    public void InvalidIpOrPort_DoesNotOverwriteLastValidEndpoint()
    {
        WithTemporaryPreferences(path =>
        {
            UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "192.16.1.33", 102);
            var expectedJson = File.ReadAllText(path);

            Assert.Throws<ArgumentException>(() =>
                UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "not-an-ip", 102));
            Assert.Throws<ArgumentException>(() =>
                UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "192.16.1.99", 0));
            Assert.Throws<ArgumentException>(() =>
                UserPreferenceStore.RecordLastDiscoveryEndpoint(path, "192.16.1.99", 65536));
            Assert.Equal(expectedJson, File.ReadAllText(path));
        });
    }

    [Fact]
    public void CorruptOrInvalidPreferenceData_FailsClosedWithoutFabricatingIp()
    {
        WithTemporaryPreferences(path =>
        {
            File.WriteAllText(path, "not-json");
            Assert.False(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(path, out _, out _));

            File.WriteAllText(path, """{"LastDiscoveryIpAddress":"not-an-ip","LastDiscoveryMmsPort":102}""");
            Assert.False(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(path, out _, out _));

            File.WriteAllText(path, """{"LastDiscoveryIpAddress":"192.16.1.33","LastDiscoveryMmsPort":65536}""");
            Assert.False(UserPreferenceStore.TryLoadLastDiscoveryEndpoint(path, out _, out _));
        });
    }

    [Fact]
    public void Dialog_UsesPersistedEndpointAheadOfCallerSelection_AndCommitsOnlyValidSubmit()
    {
        var wizard = File.ReadAllText(Find("IpConnectWizardWindow.xaml.cs"));
        var xaml = File.ReadAllText(Find("IpConnectWizardWindow.xaml"));
        var mainWindow = File.ReadAllText(Find("MainWindow.xaml.cs"));
        Assert.Contains("new IpConnectWizardWindow(initialIp, initialPort, preferLastDiscoveryEndpoint: true)", mainWindow);
        Assert.Contains("new IpConnectWizardWindow(string.Empty, device.Port <= 0 ? 102 : device.Port)", mainWindow);
        Assert.DoesNotContain("private string _newDeviceIp = \"192.168.1.10\";", mainWindow);
        Assert.Contains("private string _newDeviceIp = string.Empty;", mainWindow);
        Assert.Contains("bool preferLastDiscoveryEndpoint = false", wizard);
        Assert.Contains("_rememberDiscoveryEndpoint = preferLastDiscoveryEndpoint;", wizard);
        Assert.Contains("if (_rememberDiscoveryEndpoint &&", wizard);
        Assert.Contains("if (_rememberDiscoveryEndpoint)", wizard);
        Assert.Contains("TryLoadLastDiscoveryEndpoint(out var lastIp, out var lastPort)", wizard);
        Assert.Contains("RelayIpAddress = lastIp;", wizard);
        Assert.Contains("MmsPortText = lastPort.ToString(CultureInfo.InvariantCulture);", wizard);
        Assert.Contains("RelayIpAddress = RelayIpBox.Text.Trim();", wizard);
        Assert.Contains("RecordLastDiscoveryEndpoint(RelayIpAddress, MmsPort)", wizard);
        var validateIp = wizard.IndexOf("System.Net.IPAddress.TryParse", StringComparison.Ordinal);
        var validatePort = wizard.IndexOf("port is <= 0 or > 65535", StringComparison.Ordinal);
        var commit = wizard.IndexOf("RecordLastDiscoveryEndpoint(RelayIpAddress, MmsPort)", StringComparison.Ordinal);
        Assert.True(validateIp >= 0 && validateIp < validatePort && validatePort < commit);
        Assert.Contains("IsEditable=\"True\" IsTextSearchEnabled=\"False\"", xaml);
        Assert.Contains("UpdateSourceTrigger=LostFocus", xaml);
    }

    private static void WithTemporaryPreferences(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "arsas-last-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            action(Path.Combine(folder, "preferences.json"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Find(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(directory.FullName, relative);
            if (File.Exists(file))
                return file;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
