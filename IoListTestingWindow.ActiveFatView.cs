using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Owns the active FAT grid projection. TestPoints remains the retained project/evidence
/// collection, while the visible grid contains only points whose FAT disposition is Included.
/// Remove/restore changes workspace membership only. TestEnabled is an operator-owned flag:
/// no background projection, refresh, reconnect or FAT lifecycle is allowed to toggle it.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool ActiveFatViewClassHandlerRegistered = RegisterActiveFatViewClassHandler();
    private readonly HashSet<IoTestPointPlan> _activeFatViewPoints = new();
    private ListCollectionView? _activeFatView;
    private IoTestIedPlan? _activeFatViewIed;
    private bool _activeFatViewInstalled;

    private static bool RegisterActiveFatViewClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ActiveFatView_Loaded));
        return true;
    }

    private static void ActiveFatView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._activeFatViewInstalled)
            return;

        window._activeFatViewInstalled = true;
        window.PropertyChanged += window.ActiveFatView_WindowPropertyChanged;
        window.Closed += window.ActiveFatView_Closed;
        window.Dispatcher.BeginInvoke(
            new Action(window.RefreshActiveFatView),
            DispatcherPriority.ContextIdle);
    }

    private void ActiveFatView_WindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedIed))
            return;

        Dispatcher.BeginInvoke(new Action(RefreshActiveFatView), DispatcherPriority.DataBind);
    }

    private void RefreshActiveFatView()
    {
        if (_fatSignalsGrid == null)
        {
            if (IsLoaded)
                Dispatcher.BeginInvoke(new Action(RefreshActiveFatView), DispatcherPriority.ContextIdle);
            return;
        }

        DetachActiveFatViewPoints();
        _activeFatViewIed = SelectedIed;

        if (_activeFatViewIed == null)
        {
            _activeFatView = null;
            _fatSignalsGrid.ItemsSource = null;
            return;
        }

        foreach (var point in _activeFatViewIed.TestPoints)
        {
            point.PropertyChanged += ActiveFatView_PointPropertyChanged;
            _activeFatViewPoints.Add(point);
        }

        _activeFatView = new ListCollectionView((IList)_activeFatViewIed.TestPoints)
        {
            Filter = item => item is IoTestPointPlan point && point.IsIncludedInFat
        };
        _fatSignalsGrid.ItemsSource = _activeFatView;
    }

    private void ActiveFatView_PointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IoTestPointPlan point ||
            e.PropertyName is not (nameof(IoTestPointPlan.FatDisposition) or nameof(IoTestPointPlan.IsIncludedInFat)))
        {
            return;
        }

        void ApplyMembershipChange()
        {
            if (!_activeFatViewPoints.Contains(point))
                return;

            // Only the active projection changes here. TestEnabled must never be changed as a
            // side effect: checked/unchecked state belongs exclusively to explicit operator input.
            _activeFatView?.Refresh();
        }

        if (Dispatcher.CheckAccess())
            ApplyMembershipChange();
        else
            Dispatcher.BeginInvoke(new Action(ApplyMembershipChange), DispatcherPriority.DataBind);
    }

    private void DetachActiveFatViewPoints()
    {
        foreach (var point in _activeFatViewPoints)
            point.PropertyChanged -= ActiveFatView_PointPropertyChanged;
        _activeFatViewPoints.Clear();
    }

    private void ActiveFatView_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= ActiveFatView_WindowPropertyChanged;
        Closed -= ActiveFatView_Closed;
        DetachActiveFatViewPoints();
        _activeFatView = null;
        _activeFatViewIed = null;
        _activeFatViewInstalled = false;
    }
}
