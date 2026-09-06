using System.Collections.Concurrent;
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
    // one buffered writer flush instead of one flush per FAT point.
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

    // Workspace close is the only path allowed to defer the physical disk barrier. The
    // controller still performs every session-state mutation on the WPF Dispatcher; only
    // the already-detached journal's durable flush/read-back verification runs on a worker.
    // Verify() can therefore return the already-computed hash-chain snapshot while that
    // close-only seal is pending, and P0Lifecycle awaits the real read-back before closing.
    private static readonly AsyncLocal<int> DeferredSealScopeDepth = new();
    private static readonly ConcurrentDictionary<string, DeferredSealState> DeferredSeals =
        new(StringComparer.OrdinalIgnoreCase);

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
            FileOptions.SequentialScan);
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

    /// <summary>
    /// Enables close-only deferred durable sealing for journals disposed synchronously by
    /// IoTestSessionController.Stop(). Normal operator Stop keeps its existing synchronous
    /// durable seal + verification semantics.
    /// </summary>
    public static IDisposable BeginDeferredSealScope()
    {
        DeferredSealScopeDepth.Value++;
        return new DeferredSealScopeLease();
    }

    /// <summary>
    /// Waits for every close-only deferred journal to finish its physical flush and complete
    /// hash-chain read-back. The FAT workspace must await this before saving/closing.
    /// </summary>
    public static async Task AwaitDeferredSealsAsync()
    {
        var snapshot = DeferredSeals.ToArray();
        if (snapshot.Length == 0)
            return;

        IoTestJournalVerificationResult[] results;
        try
        {
            results = await Task.WhenAll(snapshot.Select(item => item.Value.Completion));
        }
        catch (Exception ex)
        {
            foreach (var item in snapshot)
                DeferredSeals.TryRemove(item.Key, out _);
            throw new InvalidOperationException($"Evidence journal sealing failed: {ex.Message}", ex);
        }

        foreach (var item in snapshot)
            DeferredSeals.TryRemove(item.Key, out _);

        var failures = results.Where(result => !result.IsValid).ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidOperationException(
                "Evidence journal integrity verification failed after durable sealing: " +
                string.Join(" | ", failures.Select(result => result.Error).Where(error => !string.IsNullOrWhiteSpace(error))));
        }
    }

    public IoTestJournalEnvelope Append(IoTestJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var envelope = AppendCore(entry);
            FlushVisible();
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
                FlushVisible();
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

    // FAT interaction paths only need the append-only journal to be immediately visible to
    // readers. A physical disk barrier on every report/recapture blocks the WPF dispatcher.
    // The ordered hash chain remains authoritative and is sealed durably once at session
    // disposal, before the controller verifies the closed evidence journal.
    private void FlushVisible() => _writer.Flush();

    private void FlushDurable()
    {
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
    }

    public static IoTestJournalVerificationResult Verify(string filePath)
    {
        var key = SealKey(filePath);
        if (DeferredSeals.TryGetValue(key, out var deferred))
        {
            if (deferred.Completion.IsCompletedSuccessfully)
                return deferred.Completion.Result;
            if (deferred.Completion.IsFaulted)
            {
                return new IoTestJournalVerificationResult(
                    false,
                    deferred.Provisional.RecordCount,
                    deferred.Provisional.LastHash,
                    deferred.Completion.Exception?.GetBaseException().Message ?? "Deferred evidence journal sealing failed.");
            }

            // Stop() is still executing on the UI thread at this point. All journal bytes
            // have already been writer-flushed on each append and the hash/count snapshot is
            // final. P0Lifecycle waits for Completion before the workspace may close.
            return deferred.Provisional;
        }

        return VerifyCore(filePath);
    }

    private static IoTestJournalVerificationResult VerifyCore(string filePath)
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

            if (DeferredSealScopeDepth.Value > 0)
            {
                var provisional = _recordCount == 0
                    ? new IoTestJournalVerificationResult(false, 0, _lastHash, "Evidence journal contains no records.")
                    : new IoTestJournalVerificationResult(true, checked((int)_recordCount), _lastHash, string.Empty);
                var key = SealKey(FilePath);
                var completion = Task.Run(SealDurablyAndVerify);
                DeferredSeals[key] = new DeferredSealState(provisional, completion);
                return;
            }

            try
            {
                FlushDurable();
            }
            finally
            {
                try
                {
                    _writer.Dispose();
                }
                finally
                {
                    _stream.Dispose();
                }
            }
        }
    }

    private IoTestJournalVerificationResult SealDurablyAndVerify()
    {
        try
        {
            lock (_sync)
            {
                try
                {
                    FlushDurable();
                }
                finally
                {
                    try
                    {
                        _writer.Dispose();
                    }
                    finally
                    {
                        _stream.Dispose();
                    }
                }
            }

            return VerifyCore(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            return new IoTestJournalVerificationResult(false, checked((int)_recordCount), _lastHash, ex.Message);
        }
    }

    private static string SealKey(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return filePath.Trim();
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

    private sealed record DeferredSealState(
        IoTestJournalVerificationResult Provisional,
        Task<IoTestJournalVerificationResult> Completion);

    private sealed class DeferredSealScopeLease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DeferredSealScopeDepth.Value = Math.Max(0, DeferredSealScopeDepth.Value - 1);
        }
    }
}
