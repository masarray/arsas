using System.Runtime.InteropServices;

namespace ArIED61850Tester;

internal static class WindowsApplicationIdentity
{
    private const string AppUserModelId = "masarray.ARSAS.IEC61850";

    internal static void Apply()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // A stable explicit identity makes Windows use the ARSAS executable icon for
        // taskbar grouping even when the app was launched by dotnet or a portable host.
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
