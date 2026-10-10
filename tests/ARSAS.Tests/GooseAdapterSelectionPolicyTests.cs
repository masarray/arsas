using System.Net;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class GooseAdapterSelectionPolicyTests
{
    private const string KmGuid = "89AAB1E2-3A41-48AC-B32F-5DD8A341BB11";
    private const string OtherGuid = "77AC42AA-5920-421E-80E0-A19BCE61B727";
    private static IPAddress Ip(string address) => IPAddress.Parse(address);

    private static GooseWindowsInterfaceSnapshot Nic(
        string id, string name, string mac, params string[] addresses) =>
        new(id, name, name, mac, addresses.Select(Ip).ToArray());

    private static GooseAdapterOption Capture(
        string name, string friendly, string mac = "", int index = 0) =>
        new() { Index = index, Name = name, FriendlyName = friendly, Description = friendly, MacAddress = mac };

    [Fact]
    public void SameHostKmTest_UsesOwnedTargetIpAndExactGuid_EvenWhenNameContainsLoopback()
    {
        var km = Nic(KmGuid, "KM_TEST Microsoft KM-TEST Loopback Adapter #2",
            "02-14-20-33-44-55", "192.16.1.13", "192.16.1.33");
        var other = Nic(OtherGuid, "Wi-Fi", "12-22-33-44-55-66", "192.168.1.4");
        var observedKm = Capture(@"\Device\NPF_{" + KmGuid + "}", "KM_TEST Loopback Adapter", index: 3);
        var loopback = Capture(@"\Device\NPF_Loopback", "Npcap Loopback Adapter", index: 0);

        var result = GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.13"), Ip("192.168.1.4"),
            new[] { km, other }, new[] { loopback, observedKm });
        Assert.True(result.IsSameHost);
        Assert.Same(observedKm, result.Adapter);
        Assert.True(result.HasAuthoritativeMatch);
        Assert.Contains("GUID", result.Evidence);
        Assert.Contains("unverified", result.Evidence);
        Assert.False(GooseAdapterSelectionPolicy.IsNpcapPseudoLoopback(observedKm));
        Assert.True(GooseAdapterSelectionPolicy.IsNpcapPseudoLoopback(loopback));
    }

    [Fact]
    public void UnmatchedKmTest_NeverFallsBackToOnlyUnrelatedPhysicalNicOrPseudoLoopback()
    {
        var km = Nic(KmGuid, "KM_TEST Loopback", "02-14-20-33-44-55", "192.16.1.13");
        var wlan = Capture(@"\Device\NPF_{" + OtherGuid + "}", "Wi-Fi", "AA-BB-CC-DD-EE-22");
        var result = GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.13"), Ip("192.16.1.13"), new[] { km },
            new[] { wlan, Capture(@"\Device\NPF_Loopback", "Npcap Loopback Adapter") });
        Assert.Null(result.Adapter);
        Assert.True(result.IsSameHost);
        Assert.Contains("no unique", result.Evidence);
    }

    [Fact]
    public void SameHostMultipleAliasesOnOneInterface_RemainsUnambiguousButDuplicateOwnersFailClosed()
    {
        var primary = Nic(KmGuid, "KM_TEST", "02-14-20-33-44-55", "192.16.1.13", "192.16.1.33");
        var device = Capture(@"\Device\NPF_{" + KmGuid + "}", "KM_TEST");
        Assert.Same(device, GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.33"), null, new[] { primary }, new[] { device }).Adapter);

        var clone = Nic(OtherGuid, "Conflicting assigned alias", "22-33-44-55-66-77", "192.16.1.33");
        var result = GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.33"), null, new[] { primary, clone }, new[] { device });
        Assert.Null(result.Adapter);
        Assert.Contains("no unique Windows adapter", result.Evidence);
    }

    [Fact]
    public void RemoteIED_UniqueWindowsRouteMayIdentifyAdapter_ButNoRouteMustNotGuess()
    {
        var ethernet = Nic(KmGuid, "Ethernet", "02:14:20:33:44:55", "10.0.0.20");
        var adapter = Capture(@"\Device\NPF_{" + KmGuid + "}", "Ethernet");
        var matched = GooseAdapterSelectionPolicy.Resolve(
            Ip("10.0.0.50"), Ip("10.0.0.20"), new[] { ethernet }, new[] { adapter });
        Assert.Same(adapter, matched.Adapter);
        Assert.False(matched.IsSameHost);

        var noRoute = GooseAdapterSelectionPolicy.Resolve(
            Ip("10.0.0.50"), null, new[] { ethernet }, new[] { adapter });
        Assert.Null(noRoute.Adapter);
    }

    [Fact]
    public void UniqueMacFallback_AcceptsUnknownCaptureGuidOnly_RejectsContradictoryGuid()
    {
        var nic = Nic(KmGuid, "KM_TEST", "02-14-20-33-44-55", "192.16.1.13");
        var noGuid = Capture("capture-opaque", "KM_TEST", "02:14:20:33:44:55");
        Assert.Same(noGuid, GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.13"), null, new[] { nic }, new[] { noGuid }).Adapter);

        var wrongGuid = Capture(@"\Device\NPF_{" + OtherGuid + "}", "KM_TEST", "02:14:20:33:44:55");
        Assert.Null(GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.13"), null, new[] { nic }, new[] { wrongGuid }).Adapter);
    }

    [Fact]
    public void DuplicateCaptureGuidAndIpLoopback_FailClosed()
    {
        var nic = Nic(KmGuid, "KM_TEST", "02-14-20-33-44-55", "192.16.1.13");
        var first = Capture(@"\Device\NPF_{" + KmGuid + "}", "KM_TEST", index: 1);
        var second = Capture(@"\Device\NPF_{" + KmGuid + "}", "KM_TEST #2", index: 2);
        Assert.Null(GooseAdapterSelectionPolicy.Resolve(
            Ip("192.16.1.13"), null, new[] { nic }, new[] { first, second }).Adapter);
        Assert.Null(GooseAdapterSelectionPolicy.Resolve(
            Ip("127.0.0.1"), Ip("127.0.0.1"), new[] { nic }, new[] { first }).Adapter);
    }

    [Fact]
    public void CardEntryKeepsReadOnlyCaptureAndNeverEquatesMmsToObservedGoose()
    {
        var source = Read("MainWindow.IedGooseQuickStart.cs");
        var subscriber = Read("MainWindow.GooseSubscriber.cs");
        Assert.Contains("GooseAdapterSelectionPolicy.Resolve", source);
        Assert.Contains("GooseAdapterSelectionPolicy.IsNpcapPseudoLoopback", source);
        Assert.DoesNotContain("Where(adapter => !LooksLikeLoopback(adapter))", subscriber);
        Assert.Contains("StartGooseSubscriber_Click(this, new RoutedEventArgs())", source);
        Assert.Contains("_pendingIedGooseAutoStart = device", source);
        Assert.Contains("GooseSubscriberRuntime.DefaultCaptureFilter", subscriber);
        Assert.Contains("Waiting for actual GOOSE frames", subscriber);
        Assert.DoesNotContain("SetTime", source);
    }

    private static string Read(string path)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null)
        {
            var file = Path.Combine(folder.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            folder = folder.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
