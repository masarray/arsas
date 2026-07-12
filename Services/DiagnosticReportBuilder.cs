using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

internal static class DiagnosticReportBuilder
{
    private sealed record TcpProbeResult(string Endpoint, string Result, string Detail);
    private sealed record AdapterIpv4(string Name, NetworkInterfaceType Type, IPAddress Address, int PrefixLength);
    private sealed record RouteAnalysis(string Source, string MatchingAdapters, string AdapterMatrix, string Note);

    public static async Task<string> BuildAsync(
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        IReadOnlyCollection<DiagnosticEntry> logs,
        Iec61850MonitorDevice? selectedDevice,
        CancellationToken cancellationToken)
    {
        var appAssembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticReportBuilder).Assembly;
        var engineAssembly = typeof(AR.Iec61850.Mms.MmsClientSession).Assembly;
        var probes = await Task.WhenAll(devices.Select(device => ProbeDeviceAsync(device, cancellationToken))).ConfigureAwait(false);
        var probeByEndpoint = probes.ToDictionary(result => result.Endpoint, StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(64 * 1024);
        builder.AppendLine("ArIED 61850 Diagnostic Report");
        builder.AppendLine("Smart IED Explorer & Monitor");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Generated local : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Generated UTC   : {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff 'UTC'}");
        builder.AppendLine($"App version     : {GetAssemblyVersion(appAssembly)}");
        builder.AppendLine($"App assembly    : {SafeAssemblyLocation(appAssembly)}");
        builder.AppendLine($"Engine version  : {GetAssemblyVersion(engineAssembly)}");
        builder.AppendLine($"Engine assembly : {SafeAssemblyLocation(engineAssembly)}");
        builder.AppendLine($"Runtime         : {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"OS              : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture    : Process={RuntimeInformation.ProcessArchitecture}, OS={RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Machine         : {Environment.MachineName}");
        builder.AppendLine($"Culture         : {System.Globalization.CultureInfo.CurrentCulture.Name}");
        builder.AppendLine($"Process path    : {Environment.ProcessPath ?? "unavailable"}");
        builder.AppendLine();

        builder.AppendLine("WORKSPACE SUMMARY");
        builder.AppendLine(new string('-', 72));
        builder.AppendLine($"IED count       : {devices.Count}");
        builder.AppendLine($"Connected       : {devices.Count(device => device.IsConnected)}");
        builder.AppendLine($"Monitoring      : {devices.Count(device => device.IsMonitoring)}");
        builder.AppendLine($"Selected IED    : {selectedDevice?.Name ?? "none"}");
        builder.AppendLine();

        builder.AppendLine("LOCAL NETWORK ADAPTERS");
        builder.AppendLine(new string('-', 72));
        AppendNetworkAdapters(builder);
        builder.AppendLine();

        builder.AppendLine("IED SESSION DETAILS");
        builder.AppendLine(new string('-', 72));
        if (devices.Count == 0)
        {
            builder.AppendLine("No IED is present in the workspace.");
        }
        else
        {
            foreach (var device in devices.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostic = device.LastDiagnosticSnapshot ?? new Iec61850DeviceDiagnosticSnapshot();
                probeByEndpoint.TryGetValue(device.EndpointText, out var probe);

                builder.AppendLine($"IED              : {device.Name}");
                builder.AppendLine($"Endpoint         : {device.EndpointText}");
                builder.AppendLine($"Connected        : {device.IsConnected}");
                builder.AppendLine($"Monitoring       : {device.IsMonitoring}");
                builder.AppendLine($"Busy             : {device.IsBusy}");
                builder.AppendLine($"Status           : {device.Status}");
                builder.AppendLine($"Detail           : {device.Detail}");
                builder.AppendLine($"Acquisition      : {device.AcquisitionMode}");
                builder.AppendLine($"Saved model      : {device.HasDiscoveryCache} ({device.SignalCount:N0} signal(s))");
                builder.AppendLine($"Selected         : live={device.SelectedLiveSignalCount:N0}, control={device.SelectedControlSignalCount:N0}");
                builder.AppendLine($"Logical Devices  : {EmptyAsUnavailable(device.LogicalDeviceSummary)}");
                builder.AppendLine($"Identity source  : {EmptyAsUnavailable(device.IdentitySource)}");
                var route = AnalyzeRoute(device.IpAddress);
                builder.AppendLine($"TCP probe        : {probe?.Result ?? "not run"} {probe?.Detail ?? string.Empty}".TrimEnd());
                builder.AppendLine($"Route source     : {route.Source}");
                builder.AppendLine($"Same-subnet NIC  : {route.MatchingAdapters}");
                builder.AppendLine($"Adapter matrix   : {route.AdapterMatrix}");
                builder.AppendLine($"Route note       : {route.Note}");
                if (probe != null &&
                    diagnostic.FailureKind.Equals("TCP_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase) &&
                    !probe.Result.Equals("REFUSED", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"Probe consistency: Previous connect was REFUSED but current probe is {probe.Result}; endpoint or network state changed between attempts.");
                }
                builder.AppendLine($"Diag captured    : {(diagnostic.CapturedAt == default ? "unavailable" : diagnostic.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))}");
                builder.AppendLine($"Diag phase       : {EmptyAsUnavailable(diagnostic.Phase)}");
                builder.AppendLine($"Failure kind     : {EmptyAsUnavailable(diagnostic.FailureKind)}");
                builder.AppendLine($"Friendly message : {EmptyAsUnavailable(diagnostic.FriendlyMessage)}");
                builder.AppendLine($"Exception        : {JoinNonEmpty(diagnostic.ExceptionType, diagnostic.ExceptionMessage)}");
                builder.AppendLine($"Connection mode  : {EmptyAsUnavailable(diagnostic.ConnectionMode)}");
                builder.AppendLine($"Native state     : {EmptyAsUnavailable(diagnostic.NativeState)}");
                builder.AppendLine($"Transport ready  : {diagnostic.TransportReady}");
                builder.AppendLine($"MMS ready        : {diagnostic.MmsReady}");
                builder.AppendLine($"Association      : {EmptyAsUnavailable(diagnostic.AssociationAttemptSummary)}");
                builder.AppendLine($"Association hex  : {Truncate(diagnostic.AssociationResponseHex, 4096)}");
                builder.AppendLine($"Discovery        : {EmptyAsUnavailable(diagnostic.DiscoverySummary)}");
                builder.AppendLine($"Discovery req    : {Truncate(diagnostic.DiscoveryRequestHex, 4096)}");
                builder.AppendLine($"Discovery resp   : {Truncate(diagnostic.DiscoveryResponseHex, 4096)}");
                builder.AppendLine(new string('-', 72));
            }
        }
        builder.AppendLine();

        builder.AppendLine("RECENT DIAGNOSTICS");
        builder.AppendLine(new string('-', 72));
        var recentLogs = logs
            .OrderBy(entry => entry.Time)
            .TakeLast(500)
            .ToArray();
        if (recentLogs.Length == 0)
        {
            builder.AppendLine("No diagnostic entries.");
        }
        else
        {
            foreach (var entry in recentLogs)
            {
                builder.Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                builder.Append(" | ");
                builder.Append((entry.Level ?? string.Empty).PadRight(5));
                builder.Append(" | ");
                builder.Append(entry.Source ?? string.Empty);
                builder.Append(" | ");
                builder.AppendLine(entry.Message ?? string.Empty);
            }
        }

        builder.AppendLine();
        builder.AppendLine("INTERPRETATION NOTE");
        builder.AppendLine(new string('-', 72));
        builder.AppendLine("TCP_CONNECTION_REFUSED means the socket was rejected before COTP/ACSE/MMS negotiation.");
        builder.AppendLine("An association-profile name such as BalancedApTitle is an attempted handshake profile, not the root cause when TCP itself is refused.");
        builder.AppendLine("Paste this complete report into the support conversation for analysis.");
        return builder.ToString();
    }


    private static RouteAnalysis AnalyzeRoute(string targetText)
    {
        if (!IPAddress.TryParse(targetText, out var target) || target.AddressFamily != AddressFamily.InterNetwork)
            return new RouteAnalysis("unavailable", "unavailable", "unavailable", "Route analysis currently supports IPv4 endpoints.");

        var adapters = GetActiveIpv4Adapters();
        var matching = adapters
            .Where(item => IsInSameSubnet(item.Address, target, item.PrefixLength))
            .Select(item => $"{item.Name}={item.Address}/{item.PrefixLength}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matrix = adapters.Length == 0
            ? "no active IPv4 adapter"
            : string.Join("; ", adapters.Select(item =>
                $"{item.Name}={item.Address}/{item.PrefixLength} " +
                (IsInSameSubnet(item.Address, target, item.PrefixLength) ? "MATCH" : "NO_MATCH")));

        string source;
        string? sourceAdapter = null;
        try
        {
            using var routeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeSocket.Connect(new IPEndPoint(target, 9));
            var local = (IPEndPoint?)routeSocket.LocalEndPoint;
            source = local?.Address.ToString() ?? "unavailable";
            sourceAdapter = adapters.FirstOrDefault(item => item.Address.Equals(local?.Address))?.Name;
            if (!string.IsNullOrWhiteSpace(sourceAdapter))
                source += $" via {sourceAdapter}";
        }
        catch (Exception ex)
        {
            source = $"unavailable ({ex.GetType().Name}: {ex.Message})";
        }

        string note;
        if (matching.Length == 0)
        {
            note = $"No active IPv4 adapter is in the same subnet as {target}. Check the PC adapter address, prefix length, VLAN, and static route.";
        }
        else if (matching.Length == 1)
        {
            note = $"{target} is directly reachable only through {matching[0]}. Ensure the IED is physically connected to that network.";
        }
        else
        {
            note = $"Multiple adapters match {target}; Windows route priority can select the wrong interface. Disable unused adapters or configure interface metrics/static routes.";
        }

        return new RouteAnalysis(
            source,
            matching.Length == 0 ? "none" : string.Join(", ", matching),
            matrix,
            note);
    }

    private static AdapterIpv4[] GetActiveIpv4Adapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                                  adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new AdapterIpv4(
                        adapter.Name,
                        adapter.NetworkInterfaceType,
                        address.Address,
                        address.PrefixLength)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Address.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<AdapterIpv4>();
        }
    }

    private static bool IsInSameSubnet(IPAddress left, IPAddress right, int prefixLength)
    {
        if (left.AddressFamily != AddressFamily.InterNetwork ||
            right.AddressFamily != AddressFamily.InterNetwork ||
            prefixLength is < 0 or > 32)
        {
            return false;
        }

        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (leftBytes[index] != rightBytes[index])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (leftBytes[fullBytes] & mask) == (rightBytes[fullBytes] & mask);
    }

    private static async Task<TcpProbeResult> ProbeDeviceAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        if (device.IsConnected)
            return new TcpProbeResult(device.EndpointText, "SKIPPED", "active MMS session already connected");

        if (string.IsNullOrWhiteSpace(device.IpAddress) || device.Port is <= 0 or > 65535)
            return new TcpProbeResult(device.EndpointText, "INVALID", "invalid endpoint");

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1800));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(device.IpAddress, device.Port, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(device.EndpointText, "OPEN", $"TCP accepted in {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return new TcpProbeResult(
                device.EndpointText,
                ex.SocketErrorCode == SocketError.ConnectionRefused ? "REFUSED" : "SOCKET_ERROR",
                $"SocketError={ex.SocketErrorCode}; {ex.Message}; {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new TcpProbeResult(device.EndpointText, "TIMEOUT", $"> {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TcpProbeResult(device.EndpointText, "FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendNetworkAdapters(StringBuilder builder)
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (adapters.Length == 0)
            {
                builder.AppendLine("No active network adapter was reported by Windows.");
                return;
            }

            foreach (var adapter in adapters)
            {
                var properties = adapter.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(address => $"{address.Address}/{address.PrefixLength}")
                    .ToArray();
                var gateways = properties.GatewayAddresses
                    .Select(gateway => gateway.Address.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value != "0.0.0.0")
                    .ToArray();

                builder.AppendLine($"{adapter.Name} | {adapter.NetworkInterfaceType} | IP={string.Join(", ", addresses)} | GW={string.Join(", ", gateways)}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"Network adapter enumeration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetAssemblyVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string SafeAssemblyLocation(Assembly assembly)
    {
        try
        {
            return string.IsNullOrWhiteSpace(assembly.Location) ? "single-file/in-memory" : assembly.Location;
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string EmptyAsUnavailable(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value.Trim();

    private static string JoinNonEmpty(params string?[] values)
    {
        var result = string.Join(": ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        return string.IsNullOrWhiteSpace(result) ? "unavailable" : result;
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + $"… [truncated {normalized.Length - maximumLength:N0} chars]";
    }
}
