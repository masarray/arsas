using System.Runtime.CompilerServices;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// UI-owned metadata projection cache. ST/MX Report updates never invalidate
/// units; exact CF publication, association, model, or SignalDefinition metadata
/// changes do. No copied observations, MMS side effects or cross-IED state.
/// </summary>
public sealed class IedBrowserUnitProjectionCache
{
    private readonly ConditionalWeakTable<Iec61850MonitorDevice, Entry> _devices = new();

    public IReadOnlyDictionary<string, string?> Resolve(Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument? model, IedBrowserCfEvidencePresentation evidence)
        => _devices.GetValue(device, static d => new Entry(d)).Resolve(device, model, evidence);

    private sealed class Entry
    {
        private long _collectionRevision;
        private long _lastCollectionRevision = -1;
        private long _lastUnitRevision = -1;
        private long _lastConfigurationVersion = -1;
        private long _lastAssociation = -1;
        private LiveIedModelDiscoveryDocument? _model;
        private LiveIedCanonicalModel? _canonical;
        private IReadOnlyDictionary<string, string?>? _units;

        public Entry(Iec61850MonitorDevice device)
        {
            // One collection subscription per IED, not one handler per signal.
            device.Signals.CollectionChanged += (_, _) =>
                Interlocked.Increment(ref _collectionRevision);
        }

        public IReadOnlyDictionary<string, string?> Resolve(Iec61850MonitorDevice device,
            LiveIedModelDiscoveryDocument? model, IedBrowserCfEvidencePresentation evidence)
        {
            var collection = Interlocked.Read(ref _collectionRevision);
            var unit = SignalDefinition.UnitMetadataRevision;
            var config = device.LiveValues.ConfigurationVersion;
            var association = device.LiveValues.Association;
            if (_units is not null &&
                ReferenceEquals(_model, model) &&
                ReferenceEquals(_canonical, device.LiveCanonicalModel) &&
                _lastCollectionRevision == collection &&
                _lastUnitRevision == unit &&
                _lastConfigurationVersion == config &&
                _lastAssociation == association)
                return _units;

            var rebuilt = evidence.BuildVerifiedUnits(device.Signals, model);
            _units = rebuilt;
            _model = model;
            _canonical = device.LiveCanonicalModel;
            _lastCollectionRevision = collection;
            _lastUnitRevision = unit;
            _lastConfigurationVersion = config;
            _lastAssociation = association;
            return rebuilt;
        }
    }
}
