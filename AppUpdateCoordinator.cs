using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

// The updater is intentionally independent of IEC 61850 session and command ownership.
// It can automatically fetch verified bytes but must never silently stop a live IED session.
internal static class AppUpdateCoordinator
{
    private static readonly TimeSpan LazyStartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BackgroundReviewInterval = TimeSpan.FromHours(1);
    private static UpdatePromptWindow? _activePrompt;
    private static int _started;

    public static async Task RunLazyAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            await Task.Delay(LazyStartupDelay, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var application = Application.Current;
                    if (application is null || application.Dispatcher.HasShutdownStarted)
                        return;

                    using var checkService = new AppUpdateService();
                    var manifest = await checkService.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
                    if (manifest is not null)
                    {
                        // Download and SHA-256 verify in the background, never on the WPF thread.
                        // The approved installation is still operator-directed: unsigned public
                        // binaries and active engineering sessions must not be shut down silently.
                        var verifiedInstaller = await checkService.DownloadInstallerAsync(
                            manifest, progress: null, cancellationToken).ConfigureAwait(false);

                        await application.Dispatcher.InvokeAsync(
                            () =>
                            {
                                if (application.MainWindow is not Window owner || !owner.IsVisible ||
                                    _activePrompt?.IsVisible == true)
                                    return;

                                // The modeless window owns its independent service lifetime.
                                var promptService = new AppUpdateService();
                                var prompt = new UpdatePromptWindow(promptService, manifest, verifiedInstaller)
                                {
                                    Owner = owner
                                };
                                _activePrompt = prompt;
                                prompt.Closed += (_, _) =>
                                {
                                    if (ReferenceEquals(_activePrompt, prompt))
                                        _activePrompt = null;
                                };
                                prompt.Show();
                            },
                            DispatcherPriority.Background,
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Network, metadata and download failures do not interrupt operators.
                    // The next bounded review may retry; no device activity is performed.
                    Debug.WriteLine($"ARSAS background update review failed: {exception}");
                }

                await Task.Delay(BackgroundReviewInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown; do not show an update UI or retry.
        }
        finally
        {
            Interlocked.Exchange(ref _started, 0);
        }
    }
}
