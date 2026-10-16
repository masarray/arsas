using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214BrowserCollapsibleReportSectionsTests
{
    [Fact]
    public void RcbFlagsCollapseWithControlBlockWhileLastReportRemainsIndependent()
    {
        var source = new[]
        {
            new IedBrowserDetailRow("Section", "Control Block attributes"),
            new IedBrowserDetailRow("Property", "Enabled / RptEna", "true"),
            new IedBrowserDetailRow("Property", "Reservation", "false"),
            new IedBrowserDetailRow("Section", "Trigger options"),
            new IedBrowserDetailRow("FlagGroup", "Trigger options"),
            new IedBrowserDetailRow("Section", "Information received in last Report"),
            new IedBrowserDetailRow("Property", "Received frame count", "1"),
            new IedBrowserDetailRow("Property", "Received DataSet", "LD/LLN0$MX$DATA"),
            new IedBrowserDetailRow("Section", "Data"),
            new IedBrowserDetailRow("DataHeader", "Name", "Value")
        };
        var model = new IedBrowserDetailProjection();
        model.Reset(source);
        Assert.Equal(source.Length, model.Rows.Count);
        var controls = Assert.Single(model.Rows, row =>
            row.Kind == "Section" && row.Name == "Control Block attributes");
        var received = Assert.Single(model.Rows, row =>
            row.Kind == "Section" && row.Name == "Information received in last Report");
        Assert.True(controls.IsExpanded);
        Assert.True(received.IsExpanded);
        Assert.Equal(4, controls.GetChildren().Count);
        Assert.Equal(2, received.GetChildren().Count);
        model.Toggle(controls);
        Assert.False(controls.IsExpanded);
        Assert.DoesNotContain(model.Rows, row => row.Name == "Enabled / RptEna");
        Assert.Contains(model.Rows, row => row.Name == "Received frame count");
        Assert.Contains(model.Rows, row => row.Name == "Data");
        model.Toggle(received);
        Assert.DoesNotContain(model.Rows, row => row.Name == "Received frame count");
        Assert.DoesNotContain(model.Rows, row => row.Name == "Trigger options");
        model.Toggle(controls);
        model.Toggle(received);
        Assert.Equal(source.Length, model.Rows.Count);
        Assert.Equal("true", Assert.Single(model.Rows,
            row => row.Name == "Enabled / RptEna").Value);
    }

    [Fact]
    public void RcbSectionCollapse_DoesNotDestroyLiveRowEvidence()
    {
        var source = new[]
        {
            new IedBrowserDetailRow("Section", "Information received in last Report"),
            new IedBrowserDetailRow("Property", "Received frame count", "1")
        };
        var model = new IedBrowserDetailProjection();
        model.Reset(source);
        var section = model.Rows[0];
        model.Toggle(section);
        // The retained projected child is refreshed from the canonical
        // ledger when the operator re-expands; no independent value cache.
        section.GetChildren()[0].Value = "2";
        model.Toggle(section);
        Assert.Equal("2", model.Rows[1].Value);
    }

    [Fact]
    public void UnrelatedSectionsAndDataRowsAreNotRecursivelyRearranged()
    {
        var source = new[]
        {
            new IedBrowserDetailRow("Section", "Settings"),
            new IedBrowserDetailRow("Data", "SG1", "0", 0),
            new IedBrowserDetailRow("Section", "Data"),
            new IedBrowserDetailRow("Data", "q", "Good")
        };
        var model = new IedBrowserDetailProjection();
        model.Reset(source);
        Assert.Equal(source, model.Rows.ToArray());
        Assert.False(model.Rows[0].HasChildren);
    }
}
