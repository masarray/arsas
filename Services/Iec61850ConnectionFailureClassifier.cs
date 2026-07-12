using System.Net.Sockets;

namespace ArIED61850Tester.Services;

internal sealed record Iec61850ConnectionFailure(
    string Kind,
    string FriendlyMessage,
    string TechnicalSummary);

internal static class Iec61850ConnectionFailureClassifier
{
    public static Iec61850ConnectionFailure Classify(
        string host,
        int port,
        Exception? exception,
        string associationSummary)
    {
        var endpoint = $"{host}:{port}";
        var combined = string.Join(" | ", new[]
        {
            exception?.ToString() ?? string.Empty,
            associationSummary ?? string.Empty
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var socketError = FindSocketError(exception);
        if (socketError == SocketError.ConnectionRefused ||
            ContainsAny(combined, "actively refused", "connection refused"))
        {
            return new Iec61850ConnectionFailure(
                "TCP_CONNECTION_REFUSED",
                $"TCP {endpoint} refused the connection before COTP/ACSE/MMS negotiation. " +
                "Check that the IED is online, IEC 61850 MMS is enabled on TCP port 102, the IP/port is correct, " +
                "no firewall is rejecting the socket, and the IED association limit is not exhausted.",
                BuildTechnicalSummary(exception, associationSummary, socketError));
        }

        if (socketError is SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable ||
            ContainsAny(combined, "timed out", "timeout", "host unreachable", "network is unreachable"))
        {
            var kind = socketError == SocketError.TimedOut || ContainsAny(combined, "timed out", "timeout")
                ? "TCP_TIMEOUT"
                : "NETWORK_UNREACHABLE";
            return new Iec61850ConnectionFailure(
                kind,
                $"TCP {endpoint} could not be reached before IEC 61850 association. " +
                "Check the selected network adapter, subnet/VLAN, route, cable, IED power, IP address, and firewall.",
                BuildTechnicalSummary(exception, associationSummary, socketError));
        }

        if (ContainsAny(combined, "peer closed", "connection reset", "forcibly closed", "socketexception"))
        {
            return new Iec61850ConnectionFailure(
                "TCP_TRANSPORT_CLOSED",
                $"The TCP connection to {endpoint} was opened or attempted, but the peer closed/reset it before IEC 61850 association completed. " +
                "Check concurrent-client limits, the IED MMS service, and whether another engineering tool already owns the available association.",
                BuildTechnicalSummary(exception, associationSummary, socketError));
        }

        if (ContainsAny(combined, "association rejected", "aare", "acse", "mms initiate", "initiate-error"))
        {
            return new Iec61850ConnectionFailure(
                "ACSE_MMS_ASSOCIATION_REJECTED",
                $"TCP transport to {endpoint} responded, but ACSE/MMS association was not accepted. " +
                "The copied diagnostic report includes the association profiles and response preview needed for protocol analysis.",
                BuildTechnicalSummary(exception, associationSummary, socketError));
        }

        return new Iec61850ConnectionFailure(
            "IEC61850_CONNECTION_FAILED",
            $"IEC 61850 connection to {endpoint} failed before a usable MMS association was established. " +
            "Use Copy Diagnostic and paste the report for detailed analysis.",
            BuildTechnicalSummary(exception, associationSummary, socketError));
    }

    private static SocketError? FindSocketError(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SocketException socketException)
                return socketException.SocketErrorCode;
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string BuildTechnicalSummary(
        Exception? exception,
        string associationSummary,
        SocketError? socketError)
    {
        var parts = new List<string>();
        if (socketError.HasValue)
            parts.Add($"SocketError={socketError.Value}");
        if (exception != null)
            parts.Add($"Exception={exception.GetType().Name}: {exception.Message}");
        if (!string.IsNullOrWhiteSpace(associationSummary))
            parts.Add($"AssociationAttempts={associationSummary}");
        return string.Join(" | ", parts);
    }
}
