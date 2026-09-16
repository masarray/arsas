namespace ARSAS.Tests;

public sealed class RcbIedScoutParityRegressionTests
{
    [Fact]
    public void SourceBackedRuntimeInstances_Suppress_Logical_Scl_Duplicate()
    {
        var source = ReadRepoFile("MainWindow.RcbExport.cs");

        Assert.Contains("logicalSourceReferences", source, StringComparison.Ordinal);
        Assert.Contains("logicalSourceReferences.Contains(key)", source, StringComparison.Ordinal);
        Assert.Contains("sourceBackedSelectionKeys", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceBackedExport_Preserves_Canonical_Scl_ReportControl_Identity()
    {
        var source = ReadRepoFile("MainWindow.RcbExport.cs");

        Assert.Contains("PreserveSourceReportControlIdentity = true", source, StringComparison.Ordinal);
        Assert.Contains("new SclReportControlSelection(row.SourceSelectionKey, row.ExportName)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RcbStatus_Is_SquareOnly_YellowForClientUse_AndGreenOtherwise()
    {
        var model = ReadRepoFile("Models/RcbExportModels.cs");
        var xaml = ReadRepoFile("RcbExportFilterWindow.xaml");

        Assert.Contains("Availability is MmsRcbOperationalAvailability.InUse or MmsRcbOperationalAvailability.UsedByCaller", model, StringComparison.Ordinal);
        Assert.Contains("IsClientOccupied ? OccupiedIndicatorBrush : ReadyIndicatorBrush", model, StringComparison.Ordinal);
        Assert.Contains("BrushFrom(234, 179, 8)", model, StringComparison.Ordinal);
        Assert.Contains("BrushFrom(22, 163, 74)", model, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"11\" Height=\"11\" CornerRadius=\"1\" Background=\"{Binding StatusBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding StatusText}\"", xaml, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
