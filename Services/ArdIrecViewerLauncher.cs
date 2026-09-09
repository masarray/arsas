using System.Diagnostics;
using System.IO;

namespace ArIED61850Tester.Services;

internal static class ArdIrecViewerLauncher
{
    private const string ViewerExecutableName = "ardirec.exe";

    public static bool TryResolveComtradeCfg(
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

    public static bool TryLaunch(string cfgPath, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(cfgPath) || !File.Exists(cfgPath))
        {
            error = "The COMTRADE CFG file is no longer available.";
            return false;
        }

        var executable = ResolveViewerExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            error =
                "The ARSAS COMTRADE Viewer component was not found. " +
                "Expected Tools\\ArdIrec\\ardirec.exe beside ARSAS, or set ARSAS_ARDIREC_PATH for a development build.";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--arsas-open");
            startInfo.ArgumentList.Add(Path.GetFullPath(cfgPath));

            if (Process.Start(startInfo) is null)
            {
                error = "Windows could not start the ARSAS COMTRADE Viewer component.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            error = $"Could not open the COMTRADE viewer: {ex.Message}";
            return false;
        }
    }

    private static string? ResolveViewerExecutable()
    {
        foreach (var environmentName in new[] { "ARSAS_ARDIREC_PATH", "ARDIREC_VIEWER_PATH" })
        {
            var configured = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return Path.GetFullPath(configured);
        }

        foreach (var candidate in EnumerateViewerCandidates())
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateViewerCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "Tools", "ArdIrec", ViewerExecutableName);
        yield return Path.Combine(baseDirectory, "ArdIrec", ViewerExecutableName);
        yield return Path.Combine(baseDirectory, ViewerExecutableName);

        var current = new DirectoryInfo(baseDirectory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            var siblingRoot = Path.Combine(current.FullName, "ardirec");
            yield return Path.Combine(siblingRoot, "build", "apps", "desktop", "Release", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "build", "apps", "desktop", "Debug", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "build", "apps", "desktop", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "build", "Release", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "build", "Debug", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "out", "build", "x64-Release", "apps", "desktop", ViewerExecutableName);
            yield return Path.Combine(siblingRoot, "out", "build", "x64-Debug", "apps", "desktop", ViewerExecutableName);
        }
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
