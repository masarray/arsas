using System.Collections;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>Numeric-aware comparison of IEC telegram text for operator grids only.</summary>
public static class IecNaturalReferenceOrder
{
    public static int Compare(string? left, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < left.Length && left[i] == '0') i++;
                while (j < right.Length && right[j] == '0') j++;
                var digitsI = i;
                var digitsJ = j;
                while (i < left.Length && char.IsAsciiDigit(left[i])) i++;
                while (j < right.Length && char.IsAsciiDigit(right[j])) j++;
                var numericI = i - digitsI;
                var numericJ = j - digitsJ;
                if (numericI != numericJ) return numericI.CompareTo(numericJ);
                var byDigits = string.Compare(left, digitsI, right, digitsJ, numericI, StringComparison.Ordinal);
                if (byDigits != 0) return byDigits;
                // Same integer: fewer leading zeros first, deterministic and stable.
                if (i - startI != j - startJ) return (i - startI).CompareTo(j - startJ);
                continue;
            }
            var a = char.ToUpperInvariant(left[i++]);
            var b = char.ToUpperInvariant(right[j++]);
            if (a != b) return a.CompareTo(b);
        }
        return left.Length == i && right.Length == j ? 0 : left.Length == i ? -1 : 1;
    }
}

public sealed class IecNaturalLiveMonitorSort : IComparer
{
    public static IecNaturalLiveMonitorSort Instance { get; } = new();

    public int Compare(object? a, object? b)
    {
        if (ReferenceEquals(a,b)) return 0;
        if (a is not Iec61850MonitorPoint first || b is not Iec61850MonitorPoint second)
            return Comparer.DefaultInvariant.Compare(a,b);
        var result = IecNaturalReferenceOrder.Compare(first.IecTelegram,second.IecTelegram);
        if (result != 0) return result;
        result = IecNaturalReferenceOrder.Compare(first.DeviceName,second.DeviceName);
        return result != 0 ? result :
            IecNaturalReferenceOrder.Compare(first.IecReference,second.IecReference);
    }
}

public sealed class IecNaturalCommandSort : IComparer
{
    public static IecNaturalCommandSort Instance { get; } = new();
    public int Compare(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is not SignalDefinition left || b is not SignalDefinition right)
            return Comparer.DefaultInvariant.Compare(a,b);
        // WPF's ListCollectionView.CustomSort is the FINAL visual order.
        // The underlying device.OperableCommandSignals is already ranked,
        // but the view's alphabetical comparer used to override that rank:
        // SG7_BCUMaster/GGIO2 sorted above SG7_BCUSystem/Bkr0XCBR.Pos.
        // Match exactly the live-verified command role priority at the view
        // boundary, while keeping natural instance order WITHIN each role.
        // This is presentation only; no rank authorizes an MMS Operate.
        var role = Iec61850ControlIdentity.CommandDockPriority(left)
            .CompareTo(Iec61850ControlIdentity.CommandDockPriority(right));
        if (role != 0) return role;
        var result = IecNaturalReferenceOrder.Compare(left.DisplayReference,right.DisplayReference);
        return result != 0 ? result : IecNaturalReferenceOrder.Compare(left.ObjectReference,right.ObjectReference);
    }
}

public sealed class IecNaturalGlobalMonitorSort : IComparer
{
    public static IecNaturalGlobalMonitorSort Instance { get; } = new();
    public int Compare(object? a, object? b)
    {
        if (ReferenceEquals(a,b)) return 0;
        if (a is not Iec61850MonitorPoint left || b is not Iec61850MonitorPoint right)
            return Comparer.DefaultInvariant.Compare(a,b);
        var device = string.Compare(left.DeviceName,right.DeviceName,StringComparison.OrdinalIgnoreCase);
        return device != 0 ? device : IecNaturalLiveMonitorSort.Instance.Compare(left,right);
    }
}
