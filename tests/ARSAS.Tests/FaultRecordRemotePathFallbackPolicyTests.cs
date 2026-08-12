using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class FaultRecordRemotePathFallbackPolicyTests
{
    [Fact]
    public void SiprotecFileNotFound_BuildsCaseSensitiveAndBarePathVariants()
    {
        var record = Record("COMTRADE/FRA00138.cfg", "COMTRADE/FRA00138.dat", "COMTRADE/FRA00138.inf");
        var candidates = FaultRecordRemotePathFallbackPolicy.BuildCandidates(record);

        Assert.Collection(
            candidates,
            candidate => Assert.Equal("COMTRADE/FRA00138.CFG", candidate.Record.Files[0].RemotePath),
            candidate => Assert.Equal("FRA00138.cfg", candidate.Record.Files[0].RemotePath),
            candidate => Assert.Equal("FRA00138.CFG", candidate.Record.Files[0].RemotePath));
        Assert.All(candidates, candidate => Assert.Equal(3, candidate.Record.Files.Count));
    }

    [Fact]
    public void RetryRequiresConfirmedFileOpenNotFoundBeforeAnyBytes()
    {
        Assert.True(FaultRecordRemotePathFallbackPolicy.ShouldTryCompatibilityPaths(new Iec61850FaultRecordDownloadResult
        {
            Message = "Confirmed-Error PDU during FileOpen: A2 05 A0 03 8B 01 07",
            BytesTransferred = 0
        }));
        Assert.False(FaultRecordRemotePathFallbackPolicy.ShouldTryCompatibilityPaths(new Iec61850FaultRecordDownloadResult
        {
            Message = "Confirmed-Error PDU during FileRead: 8B 01 07",
            BytesTransferred = 0
        }));
        Assert.False(FaultRecordRemotePathFallbackPolicy.ShouldTryCompatibilityPaths(new Iec61850FaultRecordDownloadResult
        {
            Message = "Confirmed-Error PDU during FileOpen: 8B 01 07",
            BytesTransferred = 128
        }));
    }

    private static Iec61850FaultRecordSet Record(params string[] paths)
    {
        var files = paths.Select(path => new Iec61850FaultRecordFile
        {
            Name = Path.GetFileName(path),
            RemotePath = path,
            RemoteDirectory = "COMTRADE",
            BaseName = Path.GetFileNameWithoutExtension(path),
            Extension = Path.GetExtension(path),
            Kind = Path.GetExtension(path).Equals(".cfg", StringComparison.OrdinalIgnoreCase)
                ? Iec61850FaultRecordFileKind.Configuration
                : Iec61850FaultRecordFileKind.Data
        }).ToArray();
        return new Iec61850FaultRecordSet
        {
            RecordId = "FRA00138",
            RemoteDirectory = "COMTRADE",
            BaseName = "FRA00138",
            Files = files,
            IsComplete = true,
            Completeness = "CFG + DAT",
            HasUnknownSize = true
        };
    }
}
