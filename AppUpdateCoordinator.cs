using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

internal static class AppUpdateCoordinator
{
    private static readonly TimeSpan LazyStartupDelay = TimeSpan.FromSeconds(15);
    private static int _started;

    public static async Task RunLazyAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            await Task.Delay(LazyStartupDelay, cancellationToken).ConfigureAwait(false);
            var application = Application.Current;
            if (application is null || application.Dispatcher.HasShutdownStarted)
                return;

            using var service = new AppUpdateService();
            var manifest = await service.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
            if (manifest is null || cancellationToken.IsCancellationRequested)
                return;

            await application.Dispatcher.InvokeAsync(
                () =>
                {
                    if (application.MainWindow is not Window owner || !owner.IsVisible)
                        return;

                    var prompt = new UpdatePromptWindow(service, manifest)
                    {
                        Owner = owner
                    };
                    prompt.ShowDialog();
                },
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Application shutdown or a cancelled background check requires no user-facing error.
        }
        catch (Exception exception)
        {
            // Update availability must never block startup or interrupt IEC 61850 work.
            Debug.WriteLine($"ARSAS lazy update check failed: {exception}");
        }
    }
}
