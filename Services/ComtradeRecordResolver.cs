using System.IO;

namespace ArIED61850Tester.Services;

/// <summary>
/// Resolves a downloaded COMTRADE CFG/DAT pair for the in-process ARSAS viewer.
/// This class has no process-launch or external-viewer responsibility.
/// </summary>
internal static class ComtradeRecordResolver
{
    public static bool TryResolveCfg(
        string localDirectory,
        string recordBaseName,
        out string cfgPath,
        out string error)
    {
        cfgPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(localDirectory) || !Directory.Exists(localDirectory))
        {
            error = "The downloaded fault-record folder is no longer available.";
            return false;
        }

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(localDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Could not inspect the downloaded COMTRADE package: {ex.Message}";
            return false;
        }

        var cfgCandidates = files
            .Where(path => string.Equals(Path.GetExtension(path), ".cfg", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (cfgCandidates.Length == 0)
        {
            error = "The downloaded package does not contain a COMTRADE CFG file.";
            return false;
        }

        var safeRecordName = SanitizeLocalFileName(recordBaseName);
        var orderedCfg = cfgCandidates
            .OrderByDescending(path =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    safeRecordName,
                    StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var cfg in orderedCfg)
        {
            var cfgStem = Path.GetFileNameWithoutExtension(cfg);
            var hasMatchingDat = files.Any(path =>
                string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileNameWithoutExtension(path), cfgStem, StringComparison.OrdinalIgnoreCase));

            if (!hasMatchingDat)
                continue;

            cfgPath = Path.GetFullPath(cfg);
            return true;
        }

        error = "A CFG file exists locally, but its matching COMTRADE DAT file was not found.";
        return false;
    }

    private static string SanitizeLocalFileName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "fault-record" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var characters = source
            .Select(character => character < ' ' || invalid.Contains(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(characters).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "fault-record" : sanitized;
    }
}
