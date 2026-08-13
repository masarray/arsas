using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public interface IIoTestEvidenceJournal : IDisposable
{
    string FilePath { get; }
    long RecordCount { get; }
    string LastHash { get; }
    IoTestJournalEnvelope Append(IoTestJournalEntry entry);

    // Existing test doubles and alternate journals keep working through this default
    // implementation. The production journal overrides it so a baseline batch performs
    // one durable disk flush instead of one fsync per FAT point.
    IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(Append).ToList();
    }
}

public sealed class IoTestEvidenceJournal : IIoTestEvidenceJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object _sync = new();
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private bool _disposed;
    private long _recordCount;
    private string _lastHash = new('0', 64);

    private IoTestEvidenceJournal(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

    public string FilePath { get; }
    public long RecordCount { get { lock (_sync) return _recordCount; } }
    public string LastHash { get { lock (_sync) return _lastHash; } }

    public static IoTestEvidenceJournal Create(
        string rootDirectory,
        IoTestProject project,
        IoTestIedPlan ied,
        Guid sessionId,
        DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);

        var projectDirectory = SanitizePathPart(project.ProjectId);
        var fileName = $"{startedAtUtc:yyyyMMddTHHmmssfffZ}_{SanitizePathPart(ied.IedName)}_{sessionId:N}.evidence.jsonl";
        return new IoTestEvidenceJournal(Path.Combine(rootDirectory, projectDirectory, fileName));
    }

    public IoTestJournalEnvelope Append(IoTestJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var envelope = AppendCore(entry);
            FlushDurable();
            return envelope;
        }
    }

    public IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var envelopes = new List<IoTestJournalEnvelope>();
            foreach (var entry in entries)
            {
                ArgumentNullException.ThrowIfNull(entry);
                envelopes.Add(AppendCore(entry));
            }

            if (envelopes.Count > 0)
                FlushDurable();
            return envelopes;
        }
    }

    private IoTestJournalEnvelope AppendCore(IoTestJournalEntry entry)
    {
        var sequence = checked(_recordCount + 1);
        var previousHash = _lastHash;
        var hashInput = JsonSerializer.SerializeToUtf8Bytes(
            new JournalHashInput(sequence, previousHash, entry),
            JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant();
        var envelope = new IoTestJournalEnvelope(sequence, previousHash, hash, entry);
        _writer.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
        _recordCount = sequence;
        _lastHash = hash;
        return envelope;
    }

    private void FlushDurable()
    {
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
    }

    public static IoTestJournalVerificationResult Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new IoTestJournalVerificationResult(false, 0, string.Empty, "Evidence journal was not found.");

        var previousHash = new string('0', 64);
        var recordCount = 0;
        try
        {
            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var envelope = JsonSerializer.Deserialize<IoTestJournalEnvelope>(line, JsonOptions)
                    ?? throw new InvalidDataException("Journal line is empty or invalid JSON.");
                var expectedSequence = recordCount + 1L;
                if (envelope.JournalSequence != expectedSequence)
                    throw new InvalidDataException($"Journal sequence {envelope.JournalSequence} was expected to be {expectedSequence}.");
                if (!string.Equals(envelope.PreviousHash, previousHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Journal previousHash mismatch at sequence {expectedSequence}.");

                var hashInput = JsonSerializer.SerializeToUtf8Bytes(
                    new JournalHashInput(envelope.JournalSequence, envelope.PreviousHash, envelope.Entry),
                    JsonOptions);
                var expectedHash = Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant();
                if (!string.Equals(envelope.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Journal hash mismatch at sequence {expectedSequence}.");

                previousHash = expectedHash;
                recordCount++;
            }

            return recordCount == 0
                ? new IoTestJournalVerificationResult(false, 0, previousHash, "Evidence journal contains no records.")
                : new IoTestJournalVerificationResult(true, recordCount, previousHash, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            return new IoTestJournalVerificationResult(false, recordCount, previousHash, ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    private static string SanitizePathPart(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "IO-TEST" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(text.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return sanitized.Length == 0 ? "IO-TEST" : sanitized;
    }

    private sealed record JournalHashInput(
        long JournalSequence,
        string PreviousHash,
        IoTestJournalEntry Entry);
}
