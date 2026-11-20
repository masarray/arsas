using System;
using System.Collections.Generic;

namespace ArIED61850Tester.Models;

/// <summary>
/// Bounded absence-only evidence for one association-scoped Browser MMS Read sweep.
/// This is NOT another values cache and cannot authorize a value, FC,
/// conversion, retry or live state. Exact reference/FC, same IED object,
/// association and projection epoch are required on every UI lookup.
/// </summary>
public enum IedBrowserReadAbsenceKind
{
    Failed,
    Interrupted,
    Deferred
}

public sealed record IedBrowserReadFailureSample(
    string Reference, string FunctionalConstraint, string Reason,
    IedBrowserReadAbsenceKind Kind = IedBrowserReadAbsenceKind.Failed);

internal sealed record IedBrowserReadAbsence(
    string Reason, IedBrowserReadAbsenceKind Kind);

public sealed class IedBrowserReadFailureEvidence
{
    private const int MaxFailureEntries = 4096;
    private readonly Iec61850MonitorDevice _owner;
    private readonly long _association;
    private readonly long _epoch;
    private readonly bool _modelBound;
    private readonly object? _model;
    private readonly object _gate = new();
    private readonly Dictionary<string, IedBrowserReadAbsence> _failed = new(StringComparer.Ordinal);

    public IedBrowserReadFailureEvidence(Iec61850MonitorDevice owner,
        bool bindToLiveModel = false)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _association = owner.LiveValues.Association;
        _epoch = owner.BrowserProjectionEpoch;
        _modelBound = bindToLiveModel;
        _model = bindToLiveModel ? owner.LiveDiscoveryModel : null;
    }

    private bool IsCurrent(Iec61850MonitorDevice candidate)
        => ReferenceEquals(_owner, candidate) && candidate.IsConnected &&
           !candidate.LiveValues.IsOffline &&
           candidate.LiveValues.Association == _association &&
           (_modelBound
               ? _model is not null && ReferenceEquals(_model, candidate.LiveDiscoveryModel)
               : candidate.BrowserProjectionEpoch == _epoch);

    // Association/epoch guard for reusing absence-only evidence across
    // consecutive bounded focused read chunks. Never carries observed values.
    public bool IsCurrentFor(Iec61850MonitorDevice candidate)
        => IsCurrent(candidate);

    private static string Key(string? reference, string? fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           (fc ?? "").Trim().ToUpperInvariant();

    public void RecordFailure(IedBrowserReadTarget target, string reason)
        => RecordAbsence(target, reason, IedBrowserReadAbsenceKind.Failed);

    /// <summary>
    /// A selected bounded initial target that never reached an MMS Read is
    /// deferred, not failed. It does not authorize a value or retry.
    /// </summary>
    public void RecordDeferred(IedBrowserReadTarget target, string reason)
        => RecordAbsence(target, reason, IedBrowserReadAbsenceKind.Deferred);

    /// <summary>A Read was started, but its terminal wire result is unknown.</summary>
    public void RecordInterrupted(IedBrowserReadTarget target, string reason)
        => RecordAbsence(target, reason, IedBrowserReadAbsenceKind.Interrupted);

    private void RecordAbsence(IedBrowserReadTarget target, string reason,
        IedBrowserReadAbsenceKind kind)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsCurrent(_owner) || string.IsNullOrWhiteSpace(reason) ||
            string.IsNullOrWhiteSpace(target.Reference) ||
            string.IsNullOrWhiteSpace(target.FunctionalConstraint))
            return;
        var key = Key(target.Reference, target.FunctionalConstraint);
        var text = reason.Trim();
        lock (_gate)
        {
            if (_failed.Count >= MaxFailureEntries && !_failed.ContainsKey(key))
                return;
            if (!IsCurrent(_owner)) return;
            // A budget-deferred marker cannot erase an actual wire failure.
            if (_failed.TryGetValue(key, out var previous) &&
                (kind == IedBrowserReadAbsenceKind.Deferred &&
                    previous.Kind != IedBrowserReadAbsenceKind.Deferred ||
                 kind == IedBrowserReadAbsenceKind.Interrupted &&
                    previous.Kind == IedBrowserReadAbsenceKind.Failed))
                return;
            _failed[key] = new IedBrowserReadAbsence(
                text.Length > 320 ? text[..320] + "…" : text, kind);
        }
    }

    public void ClearFailure(IedBrowserReadTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsCurrent(_owner))
            lock (_gate)
                _failed.Remove(Key(target.Reference, target.FunctionalConstraint));
    }

    /// <summary>
    /// Bounded immutable sample of error-only evidence from the current IED,
    /// association and verified model/epoch. No process values or MMS IO.
    /// </summary>
    public IReadOnlyList<IedBrowserReadFailureSample> SnapshotCurrent(
        Iec61850MonitorDevice device, int maxEntries = 12)
    {
        if (maxEntries <= 0 || !IsCurrent(device))
            return Array.Empty<IedBrowserReadFailureSample>();
        lock (_gate)
        {
            if (!IsCurrent(device))
                return Array.Empty<IedBrowserReadFailureSample>();
            return _failed.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Take(Math.Min(maxEntries, 32))
                .Select(item =>
                {
                    var separator = item.Key.LastIndexOf('|');
                    return new IedBrowserReadFailureSample(
                        item.Key[..separator], item.Key[(separator + 1)..],
                        item.Value.Reason, item.Value.Kind);
                })
                .ToArray();
        }
    }

    public bool TryGet(Iec61850MonitorDevice device,
        string reference, string fallbackReference, string fc, out string reason)
        => TryGet(device, reference, fallbackReference, fc, out reason, out _);

    public bool TryGet(Iec61850MonitorDevice device,
        string reference, string fallbackReference, string fc, out string reason,
        out IedBrowserReadAbsenceKind kind)
    {
        reason = "";
        kind = IedBrowserReadAbsenceKind.Failed;
        if (!IsCurrent(device) || string.IsNullOrWhiteSpace(fc))
            return false;
        // A fallback is permitted only when structured projection declared it
        // on this exact UI row. No prefix/sibling/DO guessing.
        lock (_gate)
        {
            if (!IsCurrent(device)) return false;
            if (!string.IsNullOrWhiteSpace(reference) &&
                _failed.TryGetValue(Key(reference, fc), out var exact))
            {
                reason = exact.Reason;
                kind = exact.Kind;
                return true;
            }
            if (string.IsNullOrWhiteSpace(fallbackReference) ||
                !_failed.TryGetValue(Key(fallbackReference, fc), out var fallback))
                return false;
            reason = fallback.Reason;
            kind = fallback.Kind;
            return true;
        }
    }
}
