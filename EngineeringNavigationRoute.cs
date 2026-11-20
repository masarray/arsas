namespace ArIED61850Tester;

/// <summary>
/// Operator header order is intentionally independent of the stable WPF
/// workspace indices. Preserve legacy bindings and passive monitors while
/// removing the redundant Live Monitor destination from operator navigation.
/// The legacy live view at index 1 has no navigable header button.
/// </summary>
public static class EngineeringNavigationRoute
{
    public const int SmvWorkspaceIndex = 8;

    // Visible header: Browser, Devices, Events, Annunciator, GOOSE, SMV,
    // Signal Testing, Diagnostics. Values are real MainTabs indices.
    private static readonly int[] WorkspaceByButton = [7, 0, 2, 3, 4, 8, 6, 5];

    public static int TabForButton(int button)
        => button is >= 0 and < 8
            ? WorkspaceByButton[button]
            : throw new ArgumentOutOfRangeException(nameof(button));

    public static int ButtonForTab(int tab)
        => tab switch
        {
            7 => 0, // IED Browser
            0 or 1 => 1, // Device Explorer and unreachable legacy Live Monitor
            2 => 2, // Event Logs
            3 => 3, // Annunciator
            4 => 4, // GOOSE Sniff
            8 => 5, // SMV
            6 => 6, // Signal Testing
            5 => 7, // Diagnostics
            _ => 1 // Unspecified internal workspace is never shown as SMV
        };
}
