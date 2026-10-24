using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Locks the operator-visible post-Write contract: editor closes, Browser
/// independently reads exact live DA(s), and only canonical SSOT projects
/// the result into the Data Model. Never copy popup previews into UI rows.
/// </summary>
public sealed class IedBrowserPostWriteSsotRegressionTests
{
    private static string Source(string file)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(file);
    }

    [Fact]
    public void AllWriteEditors_AutoCloseAndHandOffAttemptedExactLeafIdentities()
    {
        var single = Source("BrowserDataWriteWindow.xaml.cs");
        var ctl = Source("ControlModelWriteWindow.xaml.cs");
        var batch = Source("BrowserBatchWriteWindow.xaml.cs");
        var legacy = Source("IedBrowserWriteWindow.xaml.cs");
        Assert.Contains("public bool WriteAttempted", single);
        Assert.Contains("WriteAssociation = _association", single);
        Assert.Contains("if (IsLoaded) Close();", single);
        Assert.Contains("public bool WriteAttempted", ctl);
        Assert.Contains("WriteAssociation = _inspectedAssociation", ctl);
        Assert.Contains("if (IsLoaded) Close();", ctl);
        Assert.Contains("AttemptedLeaves = steps.Select(", batch);
        Assert.Contains("if (AttemptedLeaves.Count > 0 && IsLoaded) Close();", batch);
        Assert.Contains("AttemptedLeaves.Add((row.Reference, row.FunctionalConstraint))", legacy);
        Assert.Contains("if (AttemptedLeaves.Count > 0 && IsLoaded) Close();", legacy);
        Assert.DoesNotContain("await ReadModelAsync();", ctl.Split("private async void Write_Click(")[1]);
        Assert.DoesNotContain("PreviewBrowserDataAttributeWriteAsync(",
            single.Split("private async void Write_Click(")[1]);
        Assert.DoesNotContain("PreviewBrowserDataAttributeWriteAsync(",
            batch.Split("private async void Write_Click(")[1].Split("private void Close_Click")[0]
                .Split("var result = await _runtime.WriteBrowserDataAttributesAsync(")[1]);
    }

    [Fact]
    public void BrowserOwner_QueuesScopedReadAfterModalNotAnotherPopupPreview()
    {
        var toolbar = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        Assert.Contains("QueueIedBrowserPostWriteRead(device, model, dialog.WriteAssociation", toolbar);
        Assert.Contains("new[] { dialog.AttemptedLeaf }", toolbar);
        Assert.Contains("dialog.AttemptedLeaves, dialog.WriteStatus", toolbar);
        Assert.Contains("IedBrowserReadValues_Click(new Button { Tag = request }", toolbar);
        Assert.Contains("_iedBrowserReadOperation is not null || device.IsBusy", toolbar);
        Assert.Contains("await Task.Delay(100);", toolbar);
        Assert.DoesNotContain("device.LiveValues.Publish(", toolbar);
        var legacy = Source("MainWindow.IedBrowser.Write.cs");
        Assert.Contains("editor.AttemptedLeaves, editor.WriteStatus", legacy);
        var pipeline = Source("MainWindow.IedBrowser.cs");
        Assert.Contains("var postWriteRequest =", pipeline);
        Assert.Contains("postWriteRequest.Matches(device)", pipeline);
        Assert.Contains("targets = postWriteRequest.Targets;", pipeline);
        Assert.Contains("postWriteRequest is null && currentPage is not null", pipeline);
        Assert.Contains("sourceDevice.LiveValues.PublishForAssociation(", pipeline);
        Assert.Contains("RefreshIedBrowserVisibleValues();", pipeline);
    }

    [Fact]
    public void PostWriteReadRequest_FailsClosedAcrossReconnectOrAmbiguousDataModel()
    {
        var source = Source("Models/IedBrowserPostWriteReadRequest.cs");
        Assert.Contains("ReferenceEquals(device.LiveDiscoveryModel, model)", source);
        Assert.Contains("device.LiveValues.Association != association", source);
        Assert.Contains("attempted.Count is < 1 or > 64", source);
        Assert.Contains("if (group.Length != 1) return null;", source);
        Assert.Contains("IedBrowserLiveValues.CanonicalReference", source);
        Assert.Contains("new IedBrowserReadTarget(attr.MmsReference", source);
        Assert.DoesNotContain("PublishForAssociation(", source);
        Assert.DoesNotContain("WriteBrowserDataAttributesAsync(", source);

        foreach (var file in new[]
        {
            "BrowserDataWriteWindow.xaml", "BrowserBatchWriteWindow.xaml",
            "ControlModelWriteWindow.xaml", "IedBrowserWriteWindow.xaml"
        }) XDocument.Parse(Source(file));
    }
}
