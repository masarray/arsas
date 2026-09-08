using System.Text;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatRemoteComtradeEvidenceTests
{
    [Fact]
    public void CaptureLatest_SelectsNewestRelayRecordWithoutRequiringDownload()
    {
        var project = BuildProject();
        var ied = project.Ieds[0];
        var older = BuildRecord("FRA00027", new DateTimeOffset(2026, 8, 12, 7, 0, 0, TimeSpan.Zero));
        var latest = BuildRecord("FRA00028", new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));

        var evidence = IoFatRemoteComtradeEvidenceService.CaptureLatest(
            storage: null,
            project,
            ied,
            new[] { older, latest });

        Assert.NotNull(evidence);
        Assert.Equal("PASS", evidence!.Verdict);
        Assert.Equal(IoFatRemoteComtradeEvidenceService.AcquisitionSource, evidence.AcquisitionSource);
        Assert.Equal("FRA00028.cfg + FRA00028.dat", ied.LatestComtradeFiles);
        Assert.Equal("FRA00028.cfg", ied.LatestComtradeRemotePath);
        Assert.Equal(latest.LastModifiedUtc, ied.LatestComtradeModifiedAtUtc);
        Assert.True(ied.HasRemoteComtradeEvidence);
        Assert.Contains("download is optional", evidence.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FatPdf_IncludesConciseLatestRemoteComtradeFileServiceEvidence()
    {
        var project = BuildProject();
        var ied = project.Ieds[0];
        IoFatRemoteComtradeEvidenceService.CaptureLatest(
            storage: null,
            project,
            ied,
            new[] { BuildRecord("FRA00028", new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero)) });

        var pdf = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 8, 12, 19, 0, 0, TimeSpan.FromHours(7)));
        var text = Encoding.ASCII.GetString(pdf);

        Assert.Contains("File Service / COMTRADE Evidence", text, StringComparison.Ordinal);
        Assert.Contains("FRA00028.cfg + FRA00028.dat", text, StringComparison.Ordinal);
        Assert.Contains("Relay modified", text, StringComparison.Ordinal);
        Assert.Contains("Evidence source", text, StringComparison.Ordinal);
        Assert.Contains("FileDirectory", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OPTIONAL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not a FAT gate", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Download", text, StringComparison.Ordinal);
    }

    private static Iec61850FaultRecordSet BuildRecord(string baseName, DateTimeOffset modified)
        => new()
        {
            RecordId = baseName,
            BaseName = baseName,
            RemoteDirectory = string.Empty,
            LastModifiedUtc = modified,
            Completeness = "CFG + DAT",
            KnownSizeBytes = 4096,
            Files =
            [
                new Iec61850FaultRecordFile
                {
                    Name = baseName + ".dat",
                    RemotePath = baseName + ".dat",
                    BaseName = baseName,
                    Extension = ".dat",
                    LastModifiedUtc = modified,
                    SizeBytes = 3072
                },
                new Iec61850FaultRecordFile
                {
                    Name = baseName + ".cfg",
                    RemotePath = baseName + ".cfg",
                    BaseName = baseName,
                    Extension = ".cfg",
                    LastModifiedUtc = modified,
                    SizeBytes = 1024
                }
            ]
        };

    private static IoTestProject BuildProject() => new()
    {
        ProjectId = "FAT-COMTRADE-TEST",
        SchemaVersion = IoTestImportValidator.SupportedSchemaVersion,
        ProjectName = "FAT COMTRADE Test",
        SourceWorkbookName = "fat.xlsx",
        SourceWorkbookSha256 = new string('a', 64),
        DocumentControl = new IoFatDocumentControl
        {
            ClientProject = "FAT COMTRADE Test",
            PurchaserDocumentNumber = "DOC-001",
            Revision = "01"
        },
        Ieds =
        [
            new IoTestIedPlan
            {
                IedName = "AA1C1F03R4",
                IpAddress = "192.168.81.70"
            }
        ]
    };
}