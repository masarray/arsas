using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester.Views;

public partial class GooseSubscriberLiteView : UserControl
{
    public static readonly RoutedEvent RefreshAdaptersRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(RefreshAdaptersRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent RefreshModelsRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(RefreshModelsRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent AdapterConfirmedEvent = EventManager.RegisterRoutedEvent(
        nameof(AdapterConfirmed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent ImportPublisherModelRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ImportPublisherModelRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent StartRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(StartRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent StopRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(StopRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public static readonly RoutedEvent ClearRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ClearRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GooseSubscriberLiteView));

    public GooseSubscriberLiteView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler RefreshAdaptersRequested
    {
        add => AddHandler(RefreshAdaptersRequestedEvent, value);
        remove => RemoveHandler(RefreshAdaptersRequestedEvent, value);
    }

    public event RoutedEventHandler RefreshModelsRequested
    {
        add => AddHandler(RefreshModelsRequestedEvent, value);
        remove => RemoveHandler(RefreshModelsRequestedEvent, value);
    }

    public event RoutedEventHandler AdapterConfirmed
    {
        add => AddHandler(AdapterConfirmedEvent, value);
        remove => RemoveHandler(AdapterConfirmedEvent, value);
    }

    public event RoutedEventHandler ImportPublisherModelRequested
    {
        add => AddHandler(ImportPublisherModelRequestedEvent, value);
        remove => RemoveHandler(ImportPublisherModelRequestedEvent, value);
    }

    public event RoutedEventHandler StartRequested
    {
        add => AddHandler(StartRequestedEvent, value);
        remove => RemoveHandler(StartRequestedEvent, value);
    }

    public event RoutedEventHandler StopRequested
    {
        add => AddHandler(StopRequestedEvent, value);
        remove => RemoveHandler(StopRequestedEvent, value);
    }

    public event RoutedEventHandler ClearRequested
    {
        add => AddHandler(ClearRequestedEvent, value);
        remove => RemoveHandler(ClearRequestedEvent, value);
    }

    private void AdapterComboBox_DropDownClosed(object sender, EventArgs e)
        => RaiseEvent(new RoutedEventArgs(AdapterConfirmedEvent, this));

    private void AdapterComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Return && sender is ComboBox { IsDropDownOpen: false })
            RaiseEvent(new RoutedEventArgs(AdapterConfirmedEvent, this));
    }

    private void ImportPublisherModel_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(ImportPublisherModelRequestedEvent, this));

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(RefreshAdaptersRequestedEvent, this));

    private void RefreshModels_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(RefreshModelsRequestedEvent, this));

    private void StartCapture_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(StartRequestedEvent, this));

    private void StopCapture_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(StopRequestedEvent, this));

    private void ClearCapture_Click(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(ClearRequestedEvent, this));
}
