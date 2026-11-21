using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using Xunit;

namespace ARSAS.Tests;

public sealed class BrowserPurgeBufSafetyTests
{
    private static LiveIedModelDiscoveryDocument Model(params LiveIedReportControlModel[] reports)
        => new() { ReportControls = reports };

    private static LiveIedReportControlModel Rcb(string reference, bool buffered)
        => new() { Reference = reference, Buffered = buffered };

    [Fact]
    public void OnlyUniqueExactBufferedRcbIsAuthorized()
    {
        var model = Model(Rcb("LD0/LLN0.BR.brc01", true));
        Assert.True(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0.BR.brc01", out var exact));
        Assert.Equal("LD0/LLN0.BR.brc01", exact);
        Assert.True(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0$BR$brc01", out var alias));
        Assert.Equal(exact, alias);
        Assert.False(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0$BR$brcb01", out _));
        Assert.True(BrowserPurgeBufPolicy.SameExactBrcbReference(
            "LD0/LLN0.BR.brc01", "LD0/LLN0$BR$brc01"));
        Assert.False(BrowserPurgeBufPolicy.SameExactBrcbReference(
            "LD0/LLN0.BR.brc01", "LD0/LLN0.RP.brc01"));
        Assert.False(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0.BR.brc02", out _));
        Assert.False(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0.BR.brc01.PurgeBuf", out _));
        Assert.False(BrowserPurgeBufPolicy.TryResolve(model,
            "LD0/LLN0.RP.brc01", out _));
        Assert.False(BrowserPurgeBufPolicy.TryResolve(null,
            "LD0/LLN0.BR.brc01", out _));
        Assert.False(BrowserPurgeBufPolicy.TryResolve(
            Model(Rcb("LD0/LLN0.BR.brc01", true), Rcb("LD0/LLN0.BR.brc01", true)),
            "LD0/LLN0.BR.brc01", out _));
    }

    [Fact]
    public void UnbufferedRcbNeverAdmitted()
    {
        Assert.False(BrowserPurgeBufPolicy.TryResolve(
            Model(Rcb("LD0/LLN0.RP.urcb01", false)),
            "LD0/LLN0.RP.urcb01", out _));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void AutomaticDynamicBrcbConfigurationIsBlocked(
        bool buffered, bool dynamicPlan, bool expected)
        => Assert.Equal(expected,
            BrowserPurgeBufPolicy.RejectAutomaticBufferedMutation(buffered, dynamicPlan));

    [Theory]
    [InlineData(false, true, "BR", "LD0/LLN0.RP.urcb01", true)]
    [InlineData(false, true, "RP", "LD0/LLN0.BR.brc01", true)]
    [InlineData(false, true, "RP", "LD0/LLN0.RP.urcb01", false)]
    [InlineData(false, false, "BR", "LD0/LLN0.BR.brc01", false)]
    public void ContradictoryLiveRcbTypeCannotBypassHistoryProtection(
        bool advertisedBuffered, bool dynamicPlan, string fc,
        string reference, bool mustBlock)
        => Assert.Equal(mustBlock,
            BrowserPurgeBufPolicy.RejectAutomaticBufferedMutation(
                advertisedBuffered, dynamicPlan, fc, reference));

    [Fact]
    public void ManualPurgeWindowUsesRealWpfClosingEvent()
    {
        var xaml = XDocument.Parse(Source("BrowserRcbPurgeWindow.xaml"));
        Assert.Equal("Window_Closing", (string?)xaml.Root?.Attribute("Closing"));
        Assert.Null(xaml.Root?.Attribute("Window_Closing"));
    }

    [Fact]
    public void PurgeIsOnlyExplicitUiActionAndNeverGenericOrAutomatic()
    {
        var client = Source("Services/NativeIec61850Client.BrowserWrite.cs");
        var runtime = Source("Services/Iec61850MonitorRuntime.BrowserWrite.cs");
        var normalReporting = Source("Services/NativeIec61850Client.cs");
        var window = Source("BrowserRcbPurgeWindow.xaml.cs");
        var browser = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        var xaml = Source("BrowserRcbPurgeWindow.xaml");
        Assert.Contains("WriteBrowserPurgeBufAsync(", client);
        Assert.Contains("MmsDataValue.Boolean(true)", client);
        Assert.Contains("var enabled = await _session.ReadSingleVariableAsync(", client);
        Assert.Contains("enabledTarget, cancellationToken", client);
        Assert.Contains("var sent = await _session.WriteSingleVariableAsync(", client);
        Assert.Contains("purgeTarget, MmsDataValue.Boolean(true)", client);
        Assert.Contains("WriteBrowserPurgeBufAsync(", runtime);
        Assert.Contains("explicitOperatorConfirmation", runtime);
        Assert.Contains("session.ReplayTracker.InvalidateForManualPurge(exactReference)", runtime);
        Assert.Contains("InvalidateForManualPurge(", Source(
            "Services/Iec61850ReportDeliveryTracker.cs"));
        Assert.Contains("BrowserPurgeBufPolicy.RejectAutomaticBufferedMutation(", normalReporting);
        var automaticStart = normalReporting.Split(
            "public async Task<NativeReportMonitorStartResult> StartReportMonitorAsync(")[1]
            .Split("public async Task<NativeReportMonitorSliceResult>")[0];
        Assert.Contains("BrowserPurgeBufPolicy.RejectAutomaticBufferedMutation(", automaticStart);
        Assert.DoesNotContain("WriteBrowserPurgeBufAsync(", automaticStart);
        Assert.DoesNotContain("\"PurgeBuf\"", automaticStart);
        Assert.Contains("ConfirmationBox.Text, \"PURGE\"", window);
        Assert.Contains("Purge_Click(", window);
        Assert.Contains("BrowserWritableSelectedRcb()", browser);
        Assert.Contains("Write PurgeBuf", xaml);
        Assert.Contains("No MMS Write sent.", xaml);
        Assert.DoesNotContain("WriteBrowserPurgeBufAsync(", Source("IedBrowserWriteWindow.xaml.cs"));
        Assert.Contains("Browser MMS Write refuses control/process/report namespaces.", client);
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
