using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public interface IIoTestEvidenceJournal : IDisposable
{
    string FilePath { get; }
    long RecordCount { get; }
    string LastHash { get; }
    IoTestJournalEnvelope Append(IoTestJournalEntry entry);

    IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(Append).ToList();
    }
}

/// <summary>
/// Append-only, hash-chained FAT evidence journal.
///
/// Critical relay-bench rule: report callbacks and WPF lifecycle actions must never perform
/// file I/O. Append/AppendBatch build the immutable hash-chain envelope in memory and enqueue
/// it to a single-reader Channel. One background writer owns StreamWriter/FileStream and
/// preserves exactly the enqueue order. Stop/close completes the queue, drains it, performs
/// one durable disk barrier and verifies the complete hash chain before durable success.
/// </summary>
public sealed class IoTestEvidenceJournal : IIoTestEvidenceJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly AsyncLocal<int> DeferredSealScopeDepth = new();
    private static readonly ConcurrentDictionary<string, DeferredSealState> DeferredSeals =
        new(StringComparer.OrdinalIgnoreCase);

    // Kept as a compatibility lifecycle scope. With the queued writer Append is already
    // non-blocking and does not flush on the caller, so Resume no longer needs special disk
    // behavior. The depth is retained to keep nested existing call sites harmless.
    private static readonly AsyncLocal<int> CoalescedFlushScopeDepth = new();

    private readonly object _sync = new();
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly Channel<IoTestJournalEnvelope> _pendingWrites;
    private readonly Task _writerPump;
    private Exception? _writerFailure;
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
        _pendingWrites = Channel.CreateUnbounded<IoTestJournalEnvelope>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        _writerPump = Task.Run(ProcessPendingWritesAsync);
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

    public static IDisposable BeginDeferredSealScope()
    {
        DeferredSealScopeDepth.Value++;
        return new DeferredSealScopeLease();
    }

    public static IDisposable BeginCoalescedVisibleFlushScope()
    {
        CoalescedFlushScopeDepth.Value++;
        return new CoalescedFlushScopeLease();
    }

    public static async Task AwaitDeferredSealsAsync()
    {
        var snapshot = DeferredSeals.ToArray();
        if (snapshot.Length == 0)
            return;

        IoTestJournalVerificationResult[] results;
        try
        {
            results = await Task.WhenAll(snapshot.Select(item => item.Value.Completion)).ConfigureAwait(false);
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
            ThrowIfWriterFailed();
            var envelope = CreateEnvelope(entry);
            QueueEnvelope(envelope);
            return envelope;
        }
    }

    public IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfWriterFailed();
            var envelopes = new List<IoTestJournalEnvelope>();
            foreach (var entry in entries)
            {
                ArgumentNullException.ThrowIfNull(entry);
                var envelope = CreateEnvelope(entry);
                QueueEnvelope(envelope);
                envelopes.Add(envelope);
            }
            return envelopes;
        }
    }

    // Hashing and pointer mutation stay synchronous and deterministic; there is deliberately
    // no StreamWriter/FileStream access anywhere on the Append caller path.
    private IoTestJournalEnvelope CreateEnvelope(IoTestJournalEntry entry)
    {
        var sequence = checked(_recordCount + 1);
        var previousHash = _lastHash;
        var hashInput = JsonSerializer.SerializeToUtf8Bytes(
            new JournalHashInput(sequence, previousHash, entry),
            JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant();
        var envelope = new IoTestJournalEnvelope(sequence, previousHash, hash, entry);
        _recordCount = sequence;
        _lastHash = hash;
        return envelope;
    }

    private void QueueEnvelope(IoTestJournalEnvelope envelope)
    {
        if (!_pendingWrites.Writer.TryWrite(envelope))
            throw new InvalidOperationException("FAT evidence writer is no longer accepting records.");
    }

    private async Task ProcessPendingWritesAsync()
    {
        try
        {
            while (await _pendingWrites.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var wroteAny = false;
                while (_pendingWrites.Reader.TryRead(out var envelope))
                {
                    _writer.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
                    wroteAny = true;
                }

                // Flush only on the background writer. This makes newly written evidence
                // visible to readers without ever stalling the WPF/report callback thread.
                if (wroteAny)
                    _writer.Flush();
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _writerFailure, ex);
            _pendingWrites.Writer.TryComplete(ex);
            throw;
        }
    }

    private void ThrowIfWriterFailed()
    {
        var failure = Volatile.Read(ref _writerFailure);
        if (failure != null)
            throw new InvalidOperationException($"FAT evidence background writer failed: {failure.Message}", failure);
    }

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

            // Structural snapshot only. Queue drain + durable flush + full read-back are
            // still running and the lifecycle caller must await AwaitDeferredSealsAsync().
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
        bool deferred;
        IoTestJournalVerificationResult provisional;
        string key;

        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _pendingWrites.Writer.TryComplete();

            provisional = _recordCount == 0
                ? new IoTestJournalVerificationResult(false, 0, _lastHash, "Evidence journal contains no records.")
                : new IoTestJournalVerificationResult(true, checked((int)_recordCount), _lastHash, string.Empty);
            key = SealKey(FilePath);
            deferred = DeferredSealScopeDepth.Value > 0;
        }

        if (deferred)
        {
            // Queue drain and all physical disk work are guaranteed to happen on a worker.
            var completion = Task.Run(SealDurablyAndVerify);
            DeferredSeals[key] = new DeferredSealState(provisional, completion);
            return;
        }

        SealDurablyAndVerify();
    }

    private IoTestJournalVerificationResult SealDurablyAndVerify()
    {
        Exception? failure = null;
        try
        {
            _writerPump.GetAwaiter().GetResult();
            ThrowIfWriterFailed();
            FlushDurable();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            try
            {
                _writer.Dispose();
            }
            catch (Exception ex) when (failure == null)
            {
                failure = ex;
            }

            try
            {
                _stream.Dispose();
            }
            catch (Exception ex) when (failure == null)
            {
                failure = ex;
            }
        }

        if (failure != null)
        {
            return new IoTestJournalVerificationResult(
                false,
                checked((int)_recordCount),
                _lastHash,
                failure.GetBaseException().Message);
        }

        return VerifyCore(FilePath);
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

    private sealed class CoalescedFlushScopeLease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CoalescedFlushScopeDepth.Value = Math.Max(0, CoalescedFlushScopeDepth.Value - 1);
        }
    }

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
