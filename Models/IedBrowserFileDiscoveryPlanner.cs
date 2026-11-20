using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Single-session MMS file inventory, optimized for a usable root listing
/// before bounded optional fault-record traversal. Actual wire operations are
/// supplied by the pinned ARIEC-backed transfer client. No duplicate decoder.
/// </summary>
public static class IedBrowserFileDiscoveryPlanner
{
    public sealed record Result(
        IedBrowserFileCatalog Files,
        string? RootWarning,
        string? CatalogWarning,
        IReadOnlyList<string> CatalogDiagnostics);

    public static async Task<Result> DiscoverAsync(
        Func<CancellationToken, Task<IReadOnlyList<MmsFileDirectoryEntry>>> readRoot,
        Func<CancellationToken, Task<Iec61850FaultRecordCatalog>> scanFaultRecords,
        CancellationToken cancellationToken,
        Func<IedBrowserFileCatalog, Task>? publishRoot = null,
        TimeSpan? scanBudget = null)
    {
        ArgumentNullException.ThrowIfNull(readRoot);
        ArgumentNullException.ThrowIfNull(scanFaultRecords);
        var budget = scanBudget ?? TimeSpan.FromSeconds(30);
        if (budget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(scanBudget));
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MmsFileDirectoryEntry> root = Array.Empty<MmsFileDirectoryEntry>();
        string? rootWarning = null;
        try
        {
            root = await readRoot(cancellationToken).ConfigureAwait(false)
                ?? Array.Empty<MmsFileDirectoryEntry>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rootWarning = "Root MMS FileDirectory: " + ex.Message;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Reuse ARIEC's exact extension/package authority even for root files.
        // One invalid remote path must not hide other actual MMS inventory.
        // On failure, show paths only; none becomes download-authorized.
        Iec61850FaultRecordCatalog rootCatalog;
        try
        {
            rootCatalog = Iec61850FaultRecordCatalogBuilder.Build(root);
        }
        catch (ArgumentException ex)
        {
            rootCatalog = new Iec61850FaultRecordCatalog
            {
                Diagnostics = ["Untrusted FileDirectory record path rejected: " + ex.Message]
            };
        }
        // Optional same-root RIO sidecars join ONLY engine-proven CFG/DAT
        // records. This compatibility seam avoids changing the historical
        // engine authority merely to transport an extra physical file.
        rootCatalog = IedBrowserRioSidecarBridge.Attach(rootCatalog, root);
        var early = new IedBrowserFileCatalog(rootCatalog, root);
        if (root.Count > 0 && publishRoot is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publishRoot(early).ConfigureAwait(false);
        }

        // No real directory candidates: the verified root list is complete.
        // Scan when root failed/was empty, or it actually advertises folders.
        if (root.Count > 0 && !root.Any(entry => entry.IsLikelyDirectory))
            return new Result(early, rootWarning, null, rootCatalog.Diagnostics);

        Iec61850FaultRecordCatalog? scanned = null;
        string? scanWarning = null;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(budget);
            try
            {
                scanned = await scanFaultRecords(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                scanWarning = "Fault-record subdirectory discovery timed out; showing verified root files.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                scanWarning = "Fault-record subdirectory discovery: " + ex.Message;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (scanned is null)
            return new Result(early, rootWarning, scanWarning, rootCatalog.Diagnostics);

        // Prefer recursively verified complete packages when present, while
        // retaining root-only complete packages if the IED's later pages fail.
        var merged = new Iec61850FaultRecordCatalog
        {
            Records = scanned.Records.Concat(rootCatalog.Records)
                .DistinctBy(record => record.RecordId, StringComparer.Ordinal)
                .ToArray(),
            DirectoriesVisited = scanned.DirectoriesVisited,
            Diagnostics = scanned.Diagnostics
        };
        // A recursive scan can supersede the early root instance. Reattach
        // only real root FileDirectory RIO paths to the chosen exact records.
        return new Result(new IedBrowserFileCatalog(
                IedBrowserRioSidecarBridge.Attach(merged, root), root),
            rootWarning, scanWarning, merged.Diagnostics);
    }
}
