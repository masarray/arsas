using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatFieldEvidenceRegressionTests
{
    [Fact]
    public async Task RestartWithNewRuntimeDeviceId_RestoresOnlyExactIedNameAndTelegram()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);

            var before = Device("runtime-before");
            var cswiBefore = Point(before, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Open [01]");
            var sfBefore = Point(before, "SF62ndCB", "ADD/GGIO5.SF62ndCB.stVal", "false");
            before.Points.Add(cswiBefore);
            before.Points.Add(sfBefore);

            var saved = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value1,
                "Open [01]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
                DateTimeOffset.Parse("2026-09-13T11:01:37.116+07:00"));
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value2,
                "Closed [10]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticTransition,
                DateTimeOffset.Parse("2026-09-13T11:01:51.329+07:00"));
            await service.SaveAsync(before, saved);

            var after = Device("runtime-after");
            var sfAfter = Point(after, "SF62ndCB", "ADD/GGIO5.SF62ndCB.stVal", "false");
            var cswiAfter = Point(after, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Closed [10]");
            after.Points.Add(sfAfter);
            after.Points.Add(cswiAfter);

            var hydration = await service.HydrateAsync(after);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);

            Assert.True(hydration.Succeeded);
            Assert.True(hydration.SnapshotFound);
            Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value1));
            Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value2));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, sfAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, sfAfter, NativeFatEvidenceField.Value2));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RecyclingDataGrid_BoundEvidenceNeverMovesFromCswiToAnotherTelegram()
    {
        RunSta(() =>
        {
            var device = Device("runtime-ui");
            for (var index = 0; index < 72; index++)
            {
                device.Points.Add(Point(
                    device,
                    $"Signal {index:00}",
                    $"ADD/GGIO{index / 8 + 1}.Ind{index:00}.stVal",
                    "false"));
            }

            var cswi = Point(device, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Closed [10]");
            device.Points.Insert(8, cswi);
            var thd = Point(device, "ThdPPV PhsBC", "VI3p1_THDHarmonics/V_MHAI1.ThdPPV.phsBC.cVal.mag.f", "0");
            device.Points.Insert(54, thd);

            var cache = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                cache,
                cswi,
                NativeFatEvidenceField.Value1,
                "Closed [10]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
                DateTimeOffset.Parse("2026-09-13T11:01:51.329+07:00"));
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                cache,
                cswi,
                NativeFatEvidenceField.Value2,
                "Open [01]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticTransition,
                DateTimeOffset.Parse("2026-09-13T11:32:48.064+07:00"));

            string Read(Iec61850MonitorPoint point, NativeFatEvidenceField field)
                => NativeFatCanonicalEvidenceOverlay.Read(cache, point, field);

            var value1 = new NativeFatEvidenceBindingColumn("Value 1", NativeFatEvidenceField.Value1, 120, Read);
            var value2 = new NativeFatEvidenceBindingColumn("Value 2", NativeFatEvidenceField.Value2, 120, Read);
            var result = new NativeFatEvidenceBindingColumn("Result", NativeFatEvidenceField.Result, 100, Read);
            var grid = new DataGrid
            {
                Width = 620,
                Height = 180,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,
                ItemsSource = device.Points
            };
            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(grid, true);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Signal",
                Binding = new Binding(nameof(Iec61850MonitorPoint.SignalName)),
                Width = 180
            });
            grid.Columns.Add(value1);
            grid.Columns.Add(value2);
            grid.Columns.Add(result);

            var window = new Window
            {
                Width = 660,
                Height = 220,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Content = grid
            };

            window.Show();
            try
            {
                Pump(grid);
                AssertRealizedRowsMatch(grid, value1, value2, result, cache);

                foreach (var target in new[]
                         {
                             device.Points[0],
                             thd,
                             device.Points[^1],
                             cswi,
                             thd,
                             device.Points[20],
                             cswi
                         })
                {
                    grid.ScrollIntoView(target);
                    grid.UpdateLayout();
                    Pump(grid);
                    AssertRealizedRowsMatch(grid, value1, value2, result, cache);
                }

                grid.ScrollIntoView(thd);
                grid.UpdateLayout();
                Pump(grid);
                Assert.Equal(string.Empty, CellText(value1, thd));
                Assert.Equal(string.Empty, CellText(value2, thd));
                Assert.Equal(string.Empty, CellText(result, thd));

                grid.ScrollIntoView(cswi);
                grid.UpdateLayout();
                Pump(grid);
                Assert.Equal("Closed [10]", CellText(value1, cswi));
                Assert.Equal("Open [01]", CellText(value2, cswi));
                Assert.Equal("OK", CellText(result, cswi));
            }
            finally
            {
                window.Close();
                Pump(grid);
            }
        });
    }

    [Fact]
    public void EvidenceBindingRuntime_UsesBindingTargetsAndHasNoRowRecycleTextPatch()
    {
        var binding = File.ReadAllText(FindRepoFile("MainWindow.NativeFatEvidenceBindingRuntime.cs"));
        var tab = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("Path = new PropertyPath(\".\")", binding, StringComparison.Ordinal);
        Assert.Contains("GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget()", binding, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceBindingColumn", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("row.DataContextChanged", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("textBlock.Text =", binding, StringComparison.Ordinal);
        Assert.Contains("FlushNativeFatEvidenceBeforeShutdown();", tab, StringComparison.Ordinal);
        Assert.False(File.Exists(FindRepoFile("MainWindow.NativeFatFieldEvidenceFixes.cs")));
    }

    [Fact]
    public void ReportLogoPlacement_WideAndSquareMarksUseHeaderSpaceWithoutDistortion()
    {
        var wide = new NativeFatReportLogo(400, 100, Array.Empty<byte>(), "wide");
        var square = new NativeFatReportLogo(256, 256, Array.Empty<byte>(), "square");

        var widePlacement = NativeFatReportLogoService.CalculatePlacement(wide);
        var squarePlacement = NativeFatReportLogoService.CalculatePlacement(square);

        Assert.InRange(widePlacement.Width, 150d, 156d);
        Assert.InRange(widePlacement.Height, 37d, 40d);
        Assert.Equal(4d, widePlacement.Width / widePlacement.Height, 6);

        Assert.Equal(42d, squarePlacement.Width, 6);
        Assert.Equal(42d, squarePlacement.Height, 6);
        Assert.True(squarePlacement.X > widePlacement.X);
        Assert.InRange(squarePlacement.TopY, 577.9d, 578.1d);
    }

    [Fact]
    public void ReportLogo_TransparentCanvasIsTrimmedBeforeAdaptiveFit()
    {
        const int width = 12;
        const int height = 10;
        var bgra = new byte[width * height * 4];
        for (var y = 3; y <= 6; y++)
        {
            for (var x = 2; x <= 9; x++)
                bgra[((y * width) + x) * 4 + 3] = 255;
        }

        var bounds = NativeFatReportLogoService.FindVisibleBounds(bgra, width, height);

        Assert.Equal(2, bounds.X);
        Assert.Equal(3, bounds.Y);
        Assert.Equal(8, bounds.Width);
        Assert.Equal(4, bounds.Height);
    }

    private static void AssertRealizedRowsMatch(
        DataGrid grid,
        NativeFatEvidenceBindingColumn value1,
        NativeFatEvidenceBindingColumn value2,
        NativeFatEvidenceBindingColumn result,
        NativeFatIedSessionCacheState cache)
    {
        foreach (var item in grid.Items.OfType<Iec61850MonitorPoint>())
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow)
                continue;

            Assert.Equal(
                NativeFatCanonicalEvidenceOverlay.Read(cache, item, NativeFatEvidenceField.Value1),
                CellText(value1, item));
            Assert.Equal(
                NativeFatCanonicalEvidenceOverlay.Read(cache, item, NativeFatEvidenceField.Value2),
                CellText(value2, item));
            Assert.Equal(
                NativeFatCanonicalEvidenceOverlay.Read(cache, item, NativeFatEvidenceField.Result),
                CellText(result, item));
        }
    }

    private static string CellText(NativeFatEvidenceBindingColumn column, Iec61850MonitorPoint point)
        => column.GetCellContent(point) switch
        {
            TextBlock block => block.Text,
            TextBox editor => editor.Text,
            _ => string.Empty
        };

    private static void Pump(DispatcherObject owner)
        => owner.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!done.Wait(TimeSpan.FromSeconds(25)))
            throw new TimeoutException("STA WPF recycling regression test exceeded 25 seconds.");

        thread.Join();
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Iec61850MonitorDevice Device(string deviceId)
        => new()
        {
            DeviceId = deviceId,
            Name = "AA1EIF06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(
        Iec61850MonitorDevice device,
        string signal,
        string reference,
        string value)
        => new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            SignalName = signal,
            IecReference = reference,
            IecDataType = "DbPos",
            Quality = "Good",
            Status = "Live",
            SourceMode = "IEC 61850 report",
            Value = value,
            DeviceTimestamp = "2026-09-13T11:01:51.329+07:00"
        };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-native-fat-field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
