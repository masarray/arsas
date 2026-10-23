using AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class P62DynamicNvlProbeRegressionTests
{
    [Fact]
    public void PinnedEngine_ExposesExactSingleMemberProbeEvidenceContract()
    {
        var evidence = new MmsDynamicDataSetProbeServiceEvidence
        {
            Service = "DefineNamedVariableList",
            Attempted = true,
            IsSuccess = false,
            InvokeId = 41,
            DataSetReference = "IEDLD0/LLN0.AR_HYB_01",
            MemberReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
            RequestHex = "AA BB CC",
            ResponseHex = string.Empty,
            StateBefore = MmsAssociationState.MmsInitiated,
            StateAfter = MmsAssociationState.MmsInitiateFailed,
            ReceiveRoutingSummary = "transport closed",
            Message = "probe transport fault"
        };

        Assert.True(evidence.Attempted);
        Assert.Equal(41, evidence.InvokeId);
        Assert.Equal("IEDLD0/LLN0.AR_HYB_01", evidence.DataSetReference);
        Assert.Equal("IEDLD0/GGIO1$ST$Ind1$stVal", evidence.MemberReference);
        Assert.Equal(MmsAssociationState.MmsInitiated, evidence.StateBefore);
        Assert.Equal(MmsAssociationState.MmsInitiateFailed, evidence.StateAfter);
        Assert.Contains("DefineNamedVariableList", evidence.Summary, StringComparison.Ordinal);
        Assert.Contains("IEDLD0/LLN0.AR_HYB_01", evidence.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MmsReportActivationFailureReason.DynamicDataSetProbeDefineFailed)]
    [InlineData(MmsReportActivationFailureReason.DynamicDataSetProbeVerificationFailed)]
    [InlineData(MmsReportActivationFailureReason.DynamicDataSetProbeDeleteFailed)]
    public void PinnedEngine_ExposesProbeFailureStages(MmsReportActivationFailureReason reason)
    {
        Assert.Contains("Probe", reason.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeDiagnostics_SurfaceProbeEvidenceWithoutAddingAnotherLiveGrid()
    {
        var runtime = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");
        var diagnostics = ReadRepoFile("MainWindow.HybridAcquisitionDiagnostics.cs");

        Assert.Contains("result.Warnings.Take(3)", runtime, StringComparison.Ordinal);
        Assert.Contains("Report plan unavailable", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", diagnostics, StringComparison.Ordinal);
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
