using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArIED61850Tester.Protocol.Acse;
using ArIED61850Tester.Protocol.Diagnostics;
using ArIED61850Tester.Protocol.Mms;
using ArIED61850Tester.Protocol.Osi;

namespace ArIED61850Tester.Protocol.Iec61850;

public enum NativeIec61850AssociationState
{
    Disconnected,
    TcpConnected,
    CotpConnected,
    AcsePending,
    MmsInitiated,
    MmsInitiateFailed
}

public sealed class NativeIec61850Session : IAsyncDisposable
{
    private readonly TpktClient _tpkt = new();
    private readonly CotpClient _cotp;
    private string _lastHost = string.Empty;
    private int _lastPort = 102;

    public NativeIec61850Session()
    {
        _cotp = new CotpClient(_tpkt);
    }

    public NativeIec61850AssociationState State { get; private set; } = NativeIec61850AssociationState.Disconnected;
    public bool IsTcpConnected => _tpkt.IsConnected;
    public bool IsTransportConnected => _tpkt.IsConnected && _cotp.IsConnected;
    public bool IsMmsInitiated => State == NativeIec61850AssociationState.MmsInitiated;
    public string LastHandshakeMessage { get; private set; } = string.Empty;
    public string LastAssociationResponseHex { get; private set; } = string.Empty;
    public IReadOnlyList<AcseAssociationAttempt> LastAssociationAttempts { get; private set; } = Array.Empty<AcseAssociationAttempt>();
    public string LastAssociationAttemptSummary => LastAssociationAttempts.Count == 0
        ? string.Empty
        : string.Join(" | ", LastAssociationAttempts.Select(a => a.Summary));
    public string LastReadRequestHex { get; private set; } = string.Empty;
    public string LastReadResponseHex { get; private set; } = string.Empty;
    public IReadOnlyList<MmsReadAttempt> LastReadAttempts { get; private set; } = Array.Empty<MmsReadAttempt>();
    public string LastReadAttemptSummary => LastReadAttempts.Count == 0
        ? string.Empty
        : string.Join(" | ", LastReadAttempts.Select(a => a.Summary));
    public string LastDiscoveryRequestHex { get; private set; } = string.Empty;
    public string LastDiscoveryResponseHex { get; private set; } = string.Empty;
    public string LastDiscoveryAttemptSummary { get; private set; } = string.Empty;
    private int _nextInvokeId = 1;

    public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        _lastHost = ipAddress;
        _lastPort = port <= 0 ? 102 : port;
        _nextInvokeId = 1;
        LastReadRequestHex = string.Empty;
        LastReadResponseHex = string.Empty;
        LastReadAttempts = Array.Empty<MmsReadAttempt>();
        LastDiscoveryRequestHex = string.Empty;
        LastDiscoveryResponseHex = string.Empty;
        LastDiscoveryAttemptSummary = string.Empty;
        await AssociateAsync(resetAssociationDiagnostics: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task AssociateAsync(bool resetAssociationDiagnostics, CancellationToken cancellationToken)
    {
        State = NativeIec61850AssociationState.Disconnected;
        if (resetAssociationDiagnostics)
        {
            LastHandshakeMessage = string.Empty;
            LastAssociationResponseHex = string.Empty;
            LastAssociationAttempts = Array.Empty<AcseAssociationAttempt>();
        }

        var attempts = new List<AcseAssociationAttempt>();
        Exception? lastTransportException = null;

        foreach (var profile in AcseMmsInitiateRequest.BuildAssociationProfiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ResetTransportAsync().ConfigureAwait(false);

            try
            {
                await _tpkt.ConnectAsync(_lastHost, _lastPort, cancellationToken).ConfigureAwait(false);
                State = NativeIec61850AssociationState.TcpConnected;

                await _cotp.ConnectAsync(cancellationToken).ConfigureAwait(false);
                State = NativeIec61850AssociationState.CotpConnected;
                LastHandshakeMessage = $"{profile.Name}: {_cotp.LastConnectionConfirm?.Message ?? "COTP connection confirmed."}";

                var result = await TryInitiateMmsAssociationAsync(profile, cancellationToken).ConfigureAwait(false);
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = profile.Name,
                    IsAccepted = result.IsAccepted,
                    Message = result.Message,
                    ResponseHexPreview = result.ResponseHexPreview
                });
                LastAssociationAttempts = attempts.ToArray();

                if (result.IsAccepted)
                {
                    State = NativeIec61850AssociationState.MmsInitiated;
                    LastHandshakeMessage = result.Message;
                    return;
                }

                State = NativeIec61850AssociationState.MmsInitiateFailed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastTransportException = ex;
                State = NativeIec61850AssociationState.MmsInitiateFailed;
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = profile.Name,
                    IsAccepted = false,
                    Message = $"{profile.Name}: transport/association exception: {ex.GetType().Name}: {ex.Message}",
                    ResponseHexPreview = LastAssociationResponseHex
                });
                LastAssociationAttempts = attempts.ToArray();
            }
        }

        await ResetTransportAsync().ConfigureAwait(false);
        State = NativeIec61850AssociationState.MmsInitiateFailed;
        LastHandshakeMessage = LastAssociationAttemptSummary;
        if (string.IsNullOrWhiteSpace(LastHandshakeMessage) && lastTransportException != null)
            LastHandshakeMessage = $"Native ACSE/MMS association failed: {lastTransportException.GetType().Name}: {lastTransportException.Message}";
    }

    public async Task<AcseMmsInitiateResult> TryInitiateMmsAssociationAsync(AcseAssociationProfile profile, CancellationToken cancellationToken)
    {
        if (!IsTransportConnected)
            throw new InvalidOperationException("Native IEC 61850 transport is not connected.");

        State = NativeIec61850AssociationState.AcsePending;
        await _cotp.SendDataAsync(profile.Payload, cancellationToken).ConfigureAwait(false);
        var response = await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
        var result = AcseMmsInitiateResult.Parse(response, profile.Name);
        LastAssociationResponseHex = result.ResponseHexPreview;
        LastHandshakeMessage = result.Message;
        return result;
    }

    public Task<AcseMmsInitiateResult> TryInitiateMmsAssociationAsync(CancellationToken cancellationToken)
    {
        var profile = AcseMmsInitiateRequest.BuildAssociationProfiles()[0];
        return TryInitiateMmsAssociationAsync(profile, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariableNamesAsync(CancellationToken cancellationToken)
    {
        if (!IsMmsInitiated)
            throw new InvalidOperationException($"Native IEC 61850 MMS association is not initiated. Current state: {State}.");

        var summary = new List<string>();
        var domainsResult = await GetNameListPagedAsync(MmsGetNameListObjectClass.Domain, null, cancellationToken).ConfigureAwait(false);
        if (!domainsResult.IsSuccess)
        {
            LastDiscoveryAttemptSummary = $"Domain GetNameList failed: {domainsResult.Message}";
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var domains = domainsResult.Names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToList();

        summary.Add($"LD/domain={domains.Count}");

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variables = await GetNameListPagedAsync(MmsGetNameListObjectClass.NamedVariable, domain, cancellationToken).ConfigureAwait(false);
            if (variables.IsSuccess)
            {
                result[domain] = variables.Names
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Take(20000)
                    .ToList();
                summary.Add($"{domain}:var={result[domain].Count}");
            }
            else
            {
                result[domain] = Array.Empty<string>();
                summary.Add($"{domain}:var=failed:{variables.Message}");
            }
        }

        LastDiscoveryAttemptSummary = "Native GetNameList discovery: " + string.Join(" | ", summary.Take(20));
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariableListNamesAsync(CancellationToken cancellationToken)
    {
        if (!IsMmsInitiated)
            throw new InvalidOperationException($"Native IEC 61850 MMS association is not initiated. Current state: {State}.");

        var domainsResult = await GetNameListPagedAsync(MmsGetNameListObjectClass.Domain, null, cancellationToken).ConfigureAwait(false);
        if (!domainsResult.IsSuccess)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domainsResult.Names.Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lists = await GetNameListPagedAsync(MmsGetNameListObjectClass.NamedVariableList, domain, cancellationToken).ConfigureAwait(false);
            result[domain] = lists.IsSuccess
                ? lists.Names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                : (IReadOnlyList<string>)Array.Empty<string>();
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariableTypeTreeNamesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> domainVariables,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var summary = new List<string>();

        foreach (var domainPair in domainVariables.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = (domainPair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(domain)) continue;

            var expanded = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = domainPair.Value
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(512)
                .ToList();

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var item in await ExpandVariableTypeTreeAsync(domain, root, maxDepth: 3, maxChildrenPerNode: 256, cancellationToken).ConfigureAwait(false))
                    expanded.Add(item);
            }

            foreach (var item in await ExpandAdaptiveSiblingLogicalNodeTypeTreesAsync(domain, roots, expanded, cancellationToken).ConfigureAwait(false))
                expanded.Add(item);

            result[domain] = expanded.ToList();
            summary.Add($"{domain}:vaa={expanded.Count}");
        }

        if (summary.Count > 0)
            LastDiscoveryAttemptSummary = "Native GetVariableAccessAttributes expansion: " + string.Join(" | ", summary.Take(20));
        return result;
    }

    private async Task<IReadOnlyList<string>> ExpandAdaptiveSiblingLogicalNodeTypeTreesAsync(
        string domain,
        IReadOnlyList<string> roots,
        IEnumerable<string> alreadyExpanded,
        CancellationToken cancellationToken)
    {
        var output = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var observed = new Dictionary<NativeLnSiblingKey, SortedSet<int>>();

        foreach (var root in roots.Concat(alreadyExpanded ?? Array.Empty<string>()))
        {
            var logicalNode = ExtractRootLogicalNodeName(root);
            if (!TryParseNativeLogicalNodeName(logicalNode, out var parts))
                continue;
            if (!ShouldAdaptiveExpandLogicalNodeClass(parts.LogicalNodeClass))
                continue;

            var key = new NativeLnSiblingKey(parts.Prefix, parts.LogicalNodeClass, parts.InstanceWidth);
            if (!observed.TryGetValue(key, out var set))
            {
                set = new SortedSet<int>();
                observed[key] = set;
            }
            set.Add(parts.Instance);
        }

        foreach (var group in observed.OrderBy(g => g.Key.Prefix, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(g => g.Key.LogicalNodeClass, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Value.Count == 0) continue;

            var instance = Math.Max(1, group.Value.Min);
            var maxInstance = Math.Max(group.Value.Max + 64, 256);
            var consecutiveMisses = 0;

            while (instance <= maxInstance && consecutiveMisses < 10)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (group.Value.Contains(instance))
                {
                    instance++;
                    consecutiveMisses = 0;
                    continue;
                }

                var ln = BuildNativeLogicalNodeName(group.Key.Prefix, group.Key.LogicalNodeClass, instance, group.Key.InstanceWidth);
                var expanded = await ExpandVariableTypeTreeAsync(domain, ln, maxDepth: 3, maxChildrenPerNode: 256, cancellationToken).ConfigureAwait(false);
                if (expanded.Count == 0)
                {
                    consecutiveMisses++;
                    instance++;
                    continue;
                }

                group.Value.Add(instance);
                consecutiveMisses = 0;
                foreach (var item in expanded)
                    output.Add(item);
                instance++;
            }
        }

        return output.ToList();
    }

    private readonly record struct NativeLnSiblingKey(string Prefix, string LogicalNodeClass, int InstanceWidth);
    private readonly record struct NativeLnNameParts(string Prefix, string LogicalNodeClass, int Instance, int InstanceWidth);

    private static string ExtractRootLogicalNodeName(string item)
    {
        var text = (item ?? string.Empty).Trim().Replace('.', '$').Trim('$');
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var slash = text.LastIndexOf('/');
        if (slash >= 0 && slash < text.Length - 1)
            text = text[(slash + 1)..];
        var first = text.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first ?? string.Empty;
    }

    private static bool ShouldAdaptiveExpandLogicalNodeClass(string logicalNodeClass)
    {
        var cls = (logicalNodeClass ?? string.Empty).Trim().ToUpperInvariant();
        return cls is "MMXU" or "MMXN" or "MSQI" or "GGIO" or
               "CSWI" or "XCBR" or "XSWI" or
               "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF";
    }

    private static bool TryParseNativeLogicalNodeName(string logicalNode, out NativeLnNameParts parts)
    {
        parts = default;
        var text = (logicalNode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var classes = new[]
        {
            "MMXU", "MMXN", "MSQI", "GGIO", "CSWI", "XCBR", "XSWI",
            "PTOC", "PTRC", "PDIF", "PDIS", "PIOC", "PTOV", "PTUV", "PTEF", "PDEF"
        };

        foreach (var cls in classes.OrderByDescending(c => c.Length))
        {
            var index = text.LastIndexOf(cls, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var prefix = text[..index];
            var suffix = text[(index + cls.Length)..];
            if (string.IsNullOrWhiteSpace(suffix) || !suffix.All(char.IsDigit)) continue;
            if (!int.TryParse(suffix, out var instance) || instance <= 0) continue;
            parts = new NativeLnNameParts(prefix, cls, instance, suffix.Length);
            return true;
        }

        return false;
    }

    private static string BuildNativeLogicalNodeName(string prefix, string logicalNodeClass, int instance, int width)
    {
        var text = instance.ToString();
        if (width > 1) text = text.PadLeft(width, '0');
        return $"{prefix}{logicalNodeClass}{text}";
    }

    private async Task<IReadOnlyList<string>> ExpandVariableTypeTreeAsync(
        string domain,
        string rootItem,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var output = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Item, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((rootItem, 0));

        while (queue.Count > 0 && visited.Count < 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (item, depth) = queue.Dequeue();
            if (string.IsNullOrWhiteSpace(item) || !visited.Add(item)) continue;

            var attrs = await GetVariableAccessAttributesAsync(domain, item, cancellationToken).ConfigureAwait(false);
            if (!attrs.IsSuccess || attrs.ComponentPaths.Count == 0)
                continue;

            var childCount = 0;
            foreach (var componentPath in attrs.ComponentPaths.Take(maxChildrenPerNode))
            {
                var combined = CombineMmsItemPath(item, componentPath);
                if (string.IsNullOrWhiteSpace(combined)) continue;
                output.Add(combined);
                childCount++;

                // Walk LN -> FC -> DO shallow branches. We do not need to read every scalar here;
                // the decoder already returns nested leaves when the IED provides the full type spec.
                if (depth < maxDepth && ShouldBrowseVariableAttributeChild(combined, depth))
                    queue.Enqueue((combined, depth + 1));
            }

            if (childCount == 0 && LooksLikeScalarMmsItem(item))
                output.Add(item);
        }

        return output.ToList();
    }

    public async Task<MmsVariableAccessAttributesResult> GetVariableAccessAttributesAsync(string domainId, string itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsMmsInitiated)
        {
            var recovered = await TryRecoverAssociationAsync(cancellationToken).ConfigureAwait(false);
            if (!recovered)
            {
                return new MmsVariableAccessAttributesResult
                {
                    IsSuccess = false,
                    ComponentPaths = Array.Empty<string>(),
                    Message = $"Native MMS association is not available for GetVariableAccessAttributes {domainId}/{itemId}. State={State}."
                };
            }
        }

        var invokeId = NextInvokeId();
        var request = MmsVariableAccessAttributesRequest.Build(invokeId, domainId, itemId);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
            var result = MmsVariableAccessAttributesResponseDecoder.Decode(response, invokeId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = $"GetVariableAccessAttributes {domainId}/{itemId}: {result.Message}";
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            LastDiscoveryAttemptSummary = $"GetVariableAccessAttributes {domainId}/{itemId} transport fault: {ex.GetType().Name}: {ex.Message}";
            return new MmsVariableAccessAttributesResult
            {
                IsSuccess = false,
                ComponentPaths = Array.Empty<string>(),
                Message = LastDiscoveryAttemptSummary,
                ResponseHexPreview = LastDiscoveryResponseHex
            };
        }
    }

    private static string CombineMmsItemPath(string parent, string childPath)
    {
        parent = (parent ?? string.Empty).Trim().Trim('$');
        childPath = (childPath ?? string.Empty).Trim().Replace('.', '$').Trim('$');
        if (string.IsNullOrWhiteSpace(parent)) return childPath;
        if (string.IsNullOrWhiteSpace(childPath)) return parent;
        if (childPath.StartsWith(parent + "$", StringComparison.OrdinalIgnoreCase) || childPath.Equals(parent, StringComparison.OrdinalIgnoreCase))
            return childPath;
        return parent + "$" + childPath;
    }

    private static bool ShouldBrowseVariableAttributeChild(string item, int depth)
    {
        var parts = item.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1) return true;     // LN -> FC/DO
        if (parts.Length == 2) return true;     // LN$MX -> DO or LN$DO -> child
        if (parts.Length == 3) return true;     // LN$MX$PhV -> nested CDC
        if (parts.Length == 4 && (parts.Contains("MX") || parts.Contains("ST"))) return true;
        return false;
    }

    private static bool LooksLikeScalarMmsItem(string item)
    {
        var lower = (item ?? string.Empty).ToLowerInvariant();
        return lower.EndsWith("$stval") || lower.EndsWith("$general") || lower.EndsWith("$posval") ||
               lower.EndsWith("$f") || lower.EndsWith("$i") || lower.EndsWith("$q") || lower.EndsWith("$t");
    }

    public async Task<MmsNameListResult> GetNameListPagedAsync(MmsGetNameListObjectClass objectClass, string? domainId, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var continueAfter = string.Empty;
        var page = 0;
        MmsNameListResult? last = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsMmsInitiated)
            {
                var recovered = await TryRecoverAssociationAsync(cancellationToken).ConfigureAwait(false);
                if (!recovered)
                {
                    return new MmsNameListResult
                    {
                        IsSuccess = false,
                        Names = names,
                        MoreFollows = false,
                        Message = $"Native MMS association is not available for GetNameList {objectClass}/{domainId}. State={State}."
                    };
                }
            }

            page++;
            var invokeId = NextInvokeId();
            var request = MmsGetNameListRequest.Build(invokeId, objectClass, domainId, string.IsNullOrWhiteSpace(continueAfter) ? null : continueAfter);
            LastDiscoveryRequestHex = HexDump.ToCompactString(request);

            try
            {
                var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
                last = MmsGetNameListResponseDecoder.Decode(response, invokeId);
                LastDiscoveryResponseHex = last.ResponseHexPreview;

                if (!last.IsSuccess)
                {
                    LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"} page {page} failed: {last.Message}";
                    if (names.Count > 0)
                    {
                        return new MmsNameListResult
                        {
                            IsSuccess = true,
                            Names = names,
                            MoreFollows = false,
                            Message = $"GetNameList stopped after {names.Count} collected name(s); trailing page failed: {last.Message}",
                            ResponseHexPreview = last.ResponseHexPreview
                        };
                    }
                    return new MmsNameListResult
                    {
                        IsSuccess = false,
                        Names = names,
                        MoreFollows = false,
                        Message = last.Message,
                        ResponseHexPreview = last.ResponseHexPreview
                    };
                }

                var before = names.Count;
                foreach (var name in last.Names)
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                var newCount = names.Count - before;

                continueAfter = last.Names.LastOrDefault() ?? continueAfter;
                LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"}: page={page}, total={names.Count}, new={newCount}, more={last.MoreFollows}, morePresent={last.MoreFollowsWasPresent}.";

                // IEC 61850/MMS encoders often omit moreFollows when it is TRUE because the ASN.1
                // default is TRUE. Older tester code treated an omitted flag as FALSE and stopped
                // after the first page, which can make large IEDs look as if only the first MMXU/GGIO
                // instance exists. Keep paging while the server returns new names; stop defensively if
                // a broken server repeats the same page.
                if (last.MoreFollows && newCount <= 0)
                {
                    LastDiscoveryAttemptSummary += " Stopped paging because the IED returned no new names.";
                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                await MarkProtocolFaultAsync().ConfigureAwait(false);
                LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"} transport fault on page {page}: {ex.GetType().Name}: {ex.Message}";
                return new MmsNameListResult
                {
                    IsSuccess = names.Count > 0,
                    Names = names,
                    MoreFollows = false,
                    Message = names.Count > 0 ? $"GetNameList collected {names.Count} name(s) before trailing transport fault: {ex.GetType().Name}: {ex.Message}" : LastDiscoveryAttemptSummary,
                    ResponseHexPreview = LastDiscoveryResponseHex
                };
            }
        }
        while (last.MoreFollows && page < 64 && !string.IsNullOrWhiteSpace(continueAfter));

        return new MmsNameListResult
        {
            IsSuccess = true,
            Names = names,
            MoreFollows = last?.MoreFollows ?? false,
            Message = $"GetNameList {objectClass}/{domainId ?? "VMD"} completed: {names.Count} name(s), pages={page}.",
            ResponseHexPreview = last?.ResponseHexPreview ?? string.Empty
        };
    }

    public async Task<MmsReadDecodeResult> ReadSingleVariableAsync(MmsObjectReference reference, string dataTypeHint, CancellationToken cancellationToken)
    {
        if (!IsMmsInitiated)
            throw new InvalidOperationException($"Native IEC 61850 MMS association is not initiated. Current state: {State}.");

        var attempts = new List<MmsReadAttempt>();
        var candidates = BuildReadCandidates(reference);
        var payloadProfiles = BuildPayloadProfiles();

        foreach (var (objectProfile, candidate) in candidates)
        {
            foreach (var payloadProfile in payloadProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsMmsInitiated)
                {
                    var recovered = await TryRecoverAssociationAsync(cancellationToken).ConfigureAwait(false);
                    if (!recovered)
                        break;
                }

                var invokeId = NextInvokeId();
                var request = MmsReadRequest.BuildSingleVariableRead(invokeId, candidate, payloadProfile);
                var requestHex = HexDump.ToCompactString(request);
                LastReadRequestHex = requestHex;

                MmsReadDecodeResult result;
                try
                {
                    var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
                    result = MmsReadResponseDecoder.DecodeSingleVariable(response, dataTypeHint, invokeId);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    result = new MmsReadDecodeResult
                    {
                        IsSuccess = false,
                        Message = $"Native MMS read transport fault after {payloadProfile}: {ex.GetType().Name}: {ex.Message}",
                        ResponseHexPreview = LastReadResponseHex
                    };
                    await MarkProtocolFaultAsync().ConfigureAwait(false);
                }

                attempts.Add(new MmsReadAttempt
                {
                    Profile = objectProfile,
                    PayloadProfile = payloadProfile,
                    Reference = candidate,
                    RequestHexPreview = requestHex,
                    Result = result
                });
                LastReadAttempts = attempts.ToArray();
                LastReadResponseHex = result.ResponseHexPreview;

                if (result.IsSuccess)
                {
                    LastHandshakeMessage = $"Native MMS Confirmed-Read succeeded using {objectProfile}/{payloadProfile}: {candidate}. {result.Message}";
                    return result;
                }

                if (!ShouldTryNextPayloadProfile(result))
                    break;
            }
        }

        LastReadAttempts = attempts;
        var last = attempts.LastOrDefault()?.Result ?? new MmsReadDecodeResult
        {
            IsSuccess = false,
            Message = "Native MMS Confirmed-Read did not return a decodable value.",
            ResponseHexPreview = LastReadResponseHex
        };
        LastHandshakeMessage = LastReadAttemptSummary;
        return last;
    }

    private static IReadOnlyList<(MmsReadProfile Profile, MmsObjectReference Reference)> BuildReadCandidates(MmsObjectReference reference)
    {
        var candidates = new List<(MmsReadProfile, MmsObjectReference)>
        {
            (MmsReadProfile.PrimaryFcNamedVariable, reference)
        };

        var noFc = reference.WithoutFunctionalConstraint();
        if (!string.Equals(noFc.Item, reference.Item, StringComparison.OrdinalIgnoreCase))
            candidates.Add((MmsReadProfile.AlternateNoFcNamedVariable, noFc));

        return candidates;
    }

    private static IReadOnlyList<MmsReadPayloadProfile> BuildPayloadProfiles() => new[]
    {
        MmsReadPayloadProfile.PresentationDataValues,
        MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult,
        MmsReadPayloadProfile.SessionDataOnly,
        MmsReadPayloadProfile.RawMmsPdu
    };

    private static bool ShouldTryNextPayloadProfile(MmsReadDecodeResult result)
    {
        if (result.IsSuccess) return false;
        var message = result.Message ?? string.Empty;

        // Access/object failures mean the envelope was understood; move to the next object-name profile.
        if (message.Contains("AccessResult.failure", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("object", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase))
            return false;

        // Presentation/profile/parser failures can be explored with the next native payload profile.
        return message.Contains("transport fault", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Expected MMS Confirmed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Reject", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Abort", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("decode failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not yet recognize", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryRecoverAssociationAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_lastHost))
            return false;

        try
        {
            await AssociateAsync(resetAssociationDiagnostics: false, cancellationToken).ConfigureAwait(false);
            return IsMmsInitiated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastHandshakeMessage = $"Native MMS read recovery association failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private int NextInvokeId()
    {
        var invokeId = Interlocked.Increment(ref _nextInvokeId);
        if (invokeId > 0x7FFF)
        {
            Interlocked.Exchange(ref _nextInvokeId, 1);
            invokeId = 1;
        }
        return invokeId;
    }

    public Task<byte[]> SendPresentationPayloadAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (!IsTransportConnected)
            throw new InvalidOperationException("Native IEC 61850 transport is not connected.");

        return SendAndReceiveAsync(payload, cancellationToken);
    }

    private async Task<byte[]> SendAndReceiveAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _cotp.SendDataAsync(payload, cancellationToken).ConfigureAwait(false);
        return await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkProtocolFaultAsync()
    {
        State = NativeIec61850AssociationState.MmsInitiateFailed;
        await ResetTransportAsync().ConfigureAwait(false);
    }

    private async ValueTask ResetTransportAsync()
    {
        _cotp.Reset();
        await _tpkt.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        State = NativeIec61850AssociationState.Disconnected;
        await ResetTransportAsync().ConfigureAwait(false);
    }
}
