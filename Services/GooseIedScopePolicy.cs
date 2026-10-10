namespace ArIED61850Tester.Services;

/// <summary>
/// Identity-scoped presentation selection only: never infer an IED owner from
/// a MAC address, an IP, or an unverified GOOSE ID suffix.
/// </summary>
public static class GooseIedScopePolicy
{
    public static bool Matches(int scopeIndex, string? selectedDeviceName,
        string? selectedSclIedName, string? publisherIedName)
    {
        if (scopeIndex == 1 ||
            (string.IsNullOrWhiteSpace(selectedDeviceName) &&
             string.IsNullOrWhiteSpace(selectedSclIedName))) return true;
        if (string.IsNullOrWhiteSpace(publisherIedName) ||
            publisherIedName.Equals("Unresolved", StringComparison.OrdinalIgnoreCase))
            return false;
        return publisherIedName.Equals(selectedDeviceName?.Trim(), StringComparison.OrdinalIgnoreCase) ||
               publisherIedName.Equals(selectedSclIedName?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
