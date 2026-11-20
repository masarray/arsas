using System.Runtime.InteropServices;
using System.Reflection;

namespace ArIED61850Tester;

internal static class WindowsApplicationIdentity
{
    private const string AppUserModelId = "masarray.ARSAS.IEC61850";
    private const string RcAppUserModelId = "masarray.ARSAS.IEC61850.RC";

    internal static string ResolveAppUserModelId(string? informationalVersion)
        => informationalVersion?.Contains("-rc.", StringComparison.OrdinalIgnoreCase) == true
            ? RcAppUserModelId
            : AppUserModelId;

    internal static void Apply()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // A stable explicit identity makes Windows use the ARSAS executable icon for
        // taskbar grouping even when the app was launched by dotnet or a portable host.
        var informationalVersion = typeof(WindowsApplicationIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _ = SetCurrentProcessExplicitAppUserModelID(ResolveAppUserModelId(informationalVersion));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
