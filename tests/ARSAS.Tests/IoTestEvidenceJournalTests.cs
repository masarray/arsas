using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestEvidenceJournalTests
{
    [Fact]
    public void Append_CreatesVerifiableHashChain()
    {
        var root = TempDirectory();
        var project = Project();
        var ied = project.Ieds[0];
        var sessionId = Guid.NewGuid();
        string path;

        using (var journal = IoTestEvidenceJournal.Create(root, project, ied, sessionId, DateTimeOffset.UtcNow))
        {
            path = journal.FilePath;
            journal.Append(Entry(project, ied, sessionId, "session_started"));
            journal.Append(Entry(project, ied, sessionId, "on_evidence"));
            Assert.Equal(2, journal.RecordCount);
            Assert.Equal(64, journal.LastHash.Length);
        }

        var verification = IoTestEvidenceJournal.Verify(path);
        Assert.True(verification.IsValid, verification.Error);
        Assert.Equal(2, verification.RecordCount);
        Assert.Equal(64, verification.LastHash.Length);
    }

    [Fact]
    public void AppendBatch_CreatesSameVerifiableOrderedHashChain()
    {
        var root = TempDirectory();
        var project = Project();
        var ied = project.Ieds[0];
        var sessionId = Guid.NewGuid();
        string path;

        using (var journal = IoTestEvidenceJournal.Create(root, project, ied, sessionId, DateTimeOffset.UtcNow))
        {
            path = journal.FilePath;
            var envelopes = journal.AppendBatch(new[]
            {
                Entry(project, ied, sessionId, "session_started"),
                Entry(project, ied, sessionId, "baseline"),
                Entry(project, ied, sessionId, "baseline")
            });

            Assert.Equal(3, envelopes.Count);
            Assert.Equal(3, journal.RecordCount);
            Assert.Equal(1, envelopes[0].JournalSequence);
            Assert.Equal(2, envelopes[1].JournalSequence);
            Assert.Equal(3, envelopes[2].JournalSequence);
            Assert.Equal(envelopes[0].Hash, envelopes[1].PreviousHash);
            Assert.Equal(envelopes[1].Hash, envelopes[2].PreviousHash);
        }

        var verification = IoTestEvidenceJournal.Verify(path);
        Assert.True(verification.IsValid, verification.Error);
        Assert.Equal(3, verification.RecordCount);
    }

    [Fact]
    public void Verify_RejectsTamperedEvidenceLine()
    {
        var root = TempDirectory();
        var project = Project();
        var ied = project.Ieds[0];
        var sessionId = Guid.NewGuid();
        string path;

        using (var journal = IoTestEvidenceJournal.Create(root, project, ied, sessionId, DateTimeOffset.UtcNow))
        {
            path = journal.FilePath;
            journal.Append(Entry(project, ied, sessionId, "session_started"));
        }

        var line = File.ReadAllText(path);
        File.WriteAllText(path, line.Replace("session_started", "session_tampered", StringComparison.Ordinal));

        var verification = IoTestEvidenceJournal.Verify(path);
        Assert.False(verification.IsValid);
        Assert.Contains("hash mismatch", verification.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static IoTestJournalEntry Entry(
        IoTestProject project,
        IoTestIedPlan ied,
        Guid sessionId,
        string eventType) => new()
    {
        EventType = eventType,
        RecordedAtUtc = new DateTimeOffset(2026, 7, 28, 8, 0, 0, TimeSpan.Zero),
        ProjectId = project.ProjectId,
        SessionId = sessionId,
        IedName = ied.IedName,
        IpAddress = ied.IpAddress,
        SourceWorkbookName = project.SourceWorkbookName,
        SourceWorkbookSha256 = project.SourceWorkbookSha256,
        Reason = "test"
    };

    private static IoTestProject Project() => new()
    {
        ProjectId = "CCPP-260728",
        SchemaVersion = "ARSAS-FAT-IO-1.0",
        ProjectName = "CCPP FAT",
        SourceWorkbookName = "CCPP.xlsx",
        SourceWorkbookSha256 = new string('a', 64),
        Ieds =
        {
            new IoTestIedPlan
            {
                IedName = "AA1C1F03R4",
                IpAddress = "192.168.81.70"
            }
        }
    };

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
