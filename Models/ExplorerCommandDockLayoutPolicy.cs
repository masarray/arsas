using System.Windows;

namespace ArIED61850Tester.Models;

/// <summary>
/// Pure WPF geometry policy. When the operator Command Dock is in the
/// persistent shared shell, the old Explorer tab row has no content and must
/// NEVER consume star height, irrespective of control count or expansion.
/// </summary>
public static class ExplorerCommandDockLayoutPolicy
{
    public static GridLength LegacyRowHeight(
        bool dockReparented, bool commandExpanded, int controlCount)
        => dockReparented || !commandExpanded || controlCount <= 5
            ? GridLength.Auto
            : new GridLength(1, GridUnitType.Star);
}
