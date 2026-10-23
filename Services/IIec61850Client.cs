using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public interface IIec61850Client : IAsyncDisposable
{
    bool IsConnected { get; }
    string ConnectionMode { get; }
    Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken);
    Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsAsync(CancellationToken cancellationToken, IProgress<IedDiscoveryProgress>? progress = null);
    Task<object?> ReadValueAsync(string objectReference, CancellationToken cancellationToken);
    Task<object?> ReadValueAsync(string objectReference, string functionalConstraint, string dataType, CancellationToken cancellationToken);
}

public interface IIec61850ControlClient
{
    Task<Iec61850ControlCapabilities> InspectControlAsync(
        SignalDefinition signal,
        CancellationToken cancellationToken);

    Task<Iec61850ControlCommandResult> ExecuteControlAsync(
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken);
}
