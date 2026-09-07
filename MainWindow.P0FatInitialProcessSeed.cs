using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Seeds FAT presentation from the shared Engineering process image. The seed is presentation
/// only: it never publishes Value1/Value2 evidence and never opens a second MMS/read path.
/// </summary>
public partial class MainWindow
{
    private int _p0FatInitialSeedGeneration;

    [ModuleInitializer]
    internal static void RegisterP0FatInitialProcessSeed()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0FatInitialProcessSeed_Loaded),
            handledEventsToo: true);
    }

    private static void P0FatInitialProcessSeed_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow fat || fat.Owner is not MainWindow owner)
            return;

        owner.ScheduleP0FatInitialProcessSeed(fat);
    }

    private void ScheduleP0FatInitialProcessSeed(IoListTestingWindow fat)
    {
        var generation = Interlocked.Increment(ref _p0FatInitialSeedGeneration);

        Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                if (generation == Volatile.Read(ref _p0FatInitialSeedGeneration) && fat.IsLoaded)
                    P0SeedFatFromEngineeringImageWithAliases(fat);
            }));

        // A window can open between report callback and Engineering UI flush. Re-seed after
        // that boundary and after initial layout. Both passes copy only the process image.
        _ = SeedAfterDelayAsync(fat, generation, 275);
        _ = SeedAfterDelayAsync(fat, generation, 650);
    }

    private async Task SeedAfterDelayAsync(IoListTestingWindow fat, int generation, int delayMs)
    {
        await Task.Delay(delayMs).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _p0FatInitialSeedGeneration))
            return;

        try
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (generation == Volatile.Read(ref _p0FatInitialSeedGeneration) && fat.IsLoaded)
                        P0SeedFatFromEngineeringImageWithAliases(fat);
                },
                DispatcherPriority.DataBind);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
