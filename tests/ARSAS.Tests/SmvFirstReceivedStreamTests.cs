using System.IO;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class SmvFirstReceivedStreamTests
{
    [Fact]
    public void NewOperatorCanReceiveAnySvWithoutMatchingSclIdentity()
    {
        var row = SmvStreamRow.AnyReceived();

        Assert.Equal("Live Ethernet (auto)", row.Source);
        Assert.Equal("-", row.AppId);
        Assert.Equal("-", row.StreamId);
        Assert.Equal("-", row.DestinationMac);
        Assert.Equal("-", row.SampleRate);
        Assert.Equal("-", row.SampleMode);
        Assert.Equal(0, row.MemberCount);
    }

    [Fact]
    public void RcConstructorAlwaysOffersAutoCaptureBeforeAnyIEDSpecificRows()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SmvViewerWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "SmvViewerWindow.xaml.cs"));
        var ctor = src.IndexOf("public SmvViewerWindow(", StringComparison.Ordinal);
        var selection = src.IndexOf("SelectedStream = Streams[0];", ctor, StringComparison.Ordinal);
        Assert.True(ctor >= 0 && selection > ctor);

        // The main #734 regression must not disappear in selective RC merges:
        // an IED without SV declarations still has one legal passive option.
        var prefix = src[ctor..selection];
        var auto = prefix.IndexOf("Streams.Add(SmvStreamRow.AnyReceived());", StringComparison.Ordinal);
        var configured = prefix.IndexOf("foreach (var row in BuildRows(device))", StringComparison.Ordinal);
        Assert.True(auto >= 0 && configured > auto);
        Assert.Contains("SelectedStream = Streams[0];", src);

        // Selective reconciliation must not overwrite the operator-approved
        // contemporary RC waveform palette with old main colors.
        Assert.Contains("Brushes.Crimson,", src);
    }
}
