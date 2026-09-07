using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Seeds FAT presentation from the already-running Engineering process image after the
/// Engineering UI-flush window has settled. This is presentation-only: it never publishes
/// Value1/Value2 evidence and never opens a second MMS/read path.
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

        // Immediate projection covers an already-settled Engineering image.
        Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                if (generation == Volatile.Read(ref _p0FatInitialSeedGeneration) && fat.IsLoaded)
                    P0RefreshFatFromEngineeringImage(fat);
            }));

        // Engineering batches process-image updates on its UI flush. A FAT window can be
        // opened between the report callback and that flush; in that case the first projection
        // legitimately sees Unknown. Re-seed after the 200 ms flush boundary and once more
        // after layout/virtualization stabilization. No evidence is emitted by this method.
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
                        P0RefreshFatFromEngineeringImage(fat);
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
