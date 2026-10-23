using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class IpConnectWizardWindow : Window
{
    public static readonly DependencyProperty RelayIpAddressProperty = DependencyProperty.Register(
        nameof(RelayIpAddress), typeof(string), typeof(IpConnectWizardWindow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MmsPortTextProperty = DependencyProperty.Register(
        nameof(MmsPortText), typeof(string), typeof(IpConnectWizardWindow),
        new PropertyMetadata("102"));

    public string RelayIpAddress
    {
        get => (string)GetValue(RelayIpAddressProperty);
        set => SetValue(RelayIpAddressProperty, value);
    }

    public string MmsPortText
    {
        get => (string)GetValue(MmsPortTextProperty);
        set => SetValue(MmsPortTextProperty, value);
    }

    private readonly bool _rememberDiscoveryEndpoint;
    private readonly ICollectionView _historyView;
    private bool _updatingOctets;
    private bool _rawAddressMode;
    private const double CollapsedDialogHeight = 308;
    private const double ExpandedDialogHeight = 372;
    public int MmsPort { get; private set; } = 102;

    // UI projection ONLY; UserPreferenceStore remains the sole persistence authority.
    public ObservableCollection<UserPreferenceStore.RecentEndpointDetail> RecentEndpointDetails { get; } = new();

    public IpConnectWizardWindow(string initialIp = "", int initialPort = 102,
        bool preferLastDiscoveryEndpoint = false)
    {
        _rememberDiscoveryEndpoint = preferLastDiscoveryEndpoint;
        InitializeComponent();

        foreach (var endpoint in UserPreferenceStore.LoadRecentEndpointDetails())
            RecentEndpointDetails.Add(endpoint);

        _historyView = CollectionViewSource.GetDefaultView(RecentEndpointDetails);
        _historyView.Filter = item =>
        {
            if (item is not UserPreferenceStore.RecentEndpointDetail e) return false;
            var term = HistorySearchBox.Text.Trim();
            return term.Length == 0 ||
                   e.IpAddress.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   e.IedName.Contains(term, StringComparison.OrdinalIgnoreCase);
        };
        HistoryListBox.ItemsSource = _historyView;

        var selectedIp = initialIp?.Trim() ?? string.Empty;
        var selectedPort = initialPort is > 0 and <= 65535 ? initialPort : 102;
        if (_rememberDiscoveryEndpoint &&
            UserPreferenceStore.TryLoadLastDiscoveryEndpoint(out var lastIp, out var lastPort))
        {
            selectedIp = lastIp;
            selectedPort = lastPort;
        }
        MmsPortText = selectedPort.ToString(CultureInfo.InvariantCulture);
        ApplyAddress(selectedIp);
        // Never conceal a nonstandard port in the collapsed expert section.
        if (selectedPort != 102)
            MoreOptionsExpander.IsExpanded = true;

        // Set final dimensions before WPF creates the HWND.
        Height = MoreOptionsExpander.IsExpanded ? ExpandedDialogHeight : CollapsedDialogHeight;

        foreach (var box in Octets)
            DataObject.AddPastingHandler(box, Octet_Pasting);

        Loaded += (_, _) =>
        {
            if (_rawAddressMode) RawIpBox.Focus();
            else Octet1Box.Focus();
        };
    }

    private TextBox[] Octets => [Octet1Box, Octet2Box, Octet3Box, Octet4Box];

    private static bool IsValidPartialOctet(string text)
        => text.Length <= 3 &&
           (text.Length == 0 ||
            (text.All(ch => ch is >= '0' and <= '9') &&
             int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
             value <= 255));

    internal static bool TryNormalizeIpv4Octets(
        IReadOnlyList<string> octets, out string address)
    {
        address = string.Empty;
        if (octets.Count != 4) return false;
        var values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var text = octets[i];
            if (string.IsNullOrEmpty(text) ||
                text.Length > 3 ||
                !text.All(ch => ch is >= '0' and <= '9') ||
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) ||
                values[i] is < 0 or > 255)
                return false;
        }
        address = string.Join(".", values);
        return IPAddress.TryParse(address, out var parsed) &&
               parsed.AddressFamily == AddressFamily.InterNetwork;
    }

    internal static bool TrySplitIpv4(string source, out string[] octets)
    {
        octets = [];
        var parts = source.Trim().Split('.');
        if (!TryNormalizeIpv4Octets(parts, out var normalized)) return false;
        octets = normalized.Split('.');
        return true;
    }

    private void ApplyAddress(string address)
    {
        RelayIpAddress = address;
        if (TrySplitIpv4(address, out var parts))
        {
            _updatingOctets = true;
            try
            {
                var boxes = Octets;
                for (int i = 0; i < 4; i++) boxes[i].Text = parts[i];
            }
            finally { _updatingOctets = false; }
            SetAddressMode(false);
        }
        else if (!string.IsNullOrWhiteSpace(address))
        {
            // Existing IPv6 history stays available without a visible mode toggle.
            RawIpBox.Text = address;
            SetAddressMode(true);
        }
        else
        {
            _updatingOctets = true;
            try { foreach (var box in Octets) box.Clear(); }
            finally { _updatingOctets = false; }
            SetAddressMode(false);
        }
        WizardStatusText.Text = string.Empty;
    }

    private void SetAddressMode(bool raw)
    {
        _rawAddressMode = raw;
        // One field and one history dropdown remain visible in both modes.
        OctetEditor.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
        RawIpBox.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
        AddressHintText.Text = raw
            ? "Enter the address exactly as provided."
            : "Example: 192.168.1.10";
    }

    private void MoreOptionsExpander_ExpandedCollapsed(object sender, RoutedEventArgs e)
    {
        // User disclosure may grow the dialog. No startup positioning here.
        if (IsLoaded && ReferenceEquals(e.OriginalSource, MoreOptionsExpander))
            Height = MoreOptionsExpander.IsExpanded ? ExpandedDialogHeight : CollapsedDialogHeight;
    }

    private void Wizard_SourceInitialized(object? sender, EventArgs e)
    {
        // HWND exists but is not yet painted. Position before ShowDialog displays.
        if (Owner is { IsVisible: true } owner &&
            owner.ActualWidth > 0 && owner.ActualHeight > 0)
        {
            var centerPx = owner.PointToScreen(
                new Point(owner.ActualWidth / 2, owner.ActualHeight / 2));
            var target = PresentationSource.FromVisual(this)?.CompositionTarget
                      ?? PresentationSource.FromVisual(owner)?.CompositionTarget;
            if (target is not null)
            {
                var centerDip = target.TransformFromDevice.Transform(centerPx);
                Left = centerDip.X - Width / 2;
                Top = centerDip.Y - Height / 2;
                return;
            }
        }

        // Standalone test/no owner: use work area.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private int OctetIndex(TextBox box) => Array.IndexOf(Octets, box);

    private void FocusOctet(int index, bool atEnd = false)
    {
        var boxes = Octets;
        if (index < 0 || index >= boxes.Length) return;
        var box = boxes[index];
        box.Focus();
        box.CaretIndex = atEnd ? box.Text.Length : 0;
        if (!atEnd) box.SelectAll();
    }

    private void Octet_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Text == ".")
        {
            e.Handled = true;
            FocusOctet(OctetIndex(box) + 1);
            return;
        }
        var replacement = box.Text.Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, e.Text);
        if (!IsValidPartialOctet(replacement))
        {
            e.Handled = true;
            WizardStatusText.Text = "Each number must be between 0 and 255.";
        }
    }

    private void Octet_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        var index = OctetIndex(box);
        if (e.Key is Key.OemPeriod or Key.Decimal)
        {
            FocusOctet(index + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.SelectionLength == 0 && box.CaretIndex == 0 && index > 0)
        {
            var previous = Octets[index - 1];
            if (previous.Text.Length != 0)
                previous.Text = previous.Text[..^1];
            FocusOctet(index - 1, atEnd: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && box.SelectionLength == 0 && box.CaretIndex == 0 && index > 0)
        {
            FocusOctet(index - 1, atEnd: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && box.SelectionLength == 0 &&
                 box.CaretIndex == box.Text.Length && index < 3)
        {
            FocusOctet(index + 1);
            e.Handled = true;
        }
    }

    private void Octet_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingOctets || sender is not TextBox box) return;
        WizardStatusText.Text = string.Empty;
        if (box.Text.Length == 3 && IsValidPartialOctet(box.Text) &&
            box.IsKeyboardFocusWithin)
            FocusOctet(OctetIndex(box) + 1);
    }

    private void Octet_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var pasted = (e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
        // A complete IPv6 paste switches editor automatically; IPv6 literals
        // without dots must not be processed as invalid single IPv4 octets.
        if (IPAddress.TryParse(pasted, out var fullAddress) &&
            fullAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            e.CancelCommand();
            ApplyAddress(pasted);
            RawIpBox.Focus();
            RawIpBox.CaretIndex = RawIpBox.Text.Length;
            return;
        }
        if (pasted.Contains('.'))
        {
            e.CancelCommand();
            if (TrySplitIpv4(pasted, out _))
            {
                ApplyAddress(pasted);
                FocusOctet(3, atEnd: true);
            }
            else
                WizardStatusText.Text = "Paste a full IP address, for example 192.168.1.10.";
            return;
        }

        var next = box.Text.Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, pasted);
        if (!IsValidPartialOctet(next))
        {
            e.CancelCommand();
            WizardStatusText.Text = "Each number must be between 0 and 255.";
        }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
        if (HistoryPopup.IsOpen)
        {
            HistorySearchBox.Clear();
            _historyView.Refresh();
            HistorySearchBox.Focus();
        }
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _historyView?.Refresh();
        if (HistoryEmptyText is not null)
            HistoryEmptyText.Visibility = _historyView?.IsEmpty == true
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HistorySearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && !_historyView.IsEmpty)
        {
            HistoryListBox.SelectedIndex = 0;
            HistoryListBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HistoryPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !_historyView.IsEmpty)
        {
            HistoryListBox.SelectedIndex = 0;
            CommitHistorySelection();
            e.Handled = true;
        }
    }

    private void HistoryListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => CommitHistorySelection();

    private void HistoryListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHistorySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HistoryPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void CommitHistorySelection()
    {
        if (HistoryListBox.SelectedItem is not UserPreferenceStore.RecentEndpointDetail selection)
            return;
        ApplyAddress(selection.IpAddress);
        // Selecting history restores the exact saved IP:port, even when unverified.
        // This is recall only; the connection still requires explicit submission.
        MmsPortText = selection.MmsPort.ToString(CultureInfo.InvariantCulture);
        if (selection.MmsPort != 102)
            MoreOptionsExpander.IsExpanded = true;
        HistoryPopup.IsOpen = false;
        if (_rawAddressMode) RawIpBox.Focus();
        else Octet1Box.Focus();
        // Recalling an endpoint is UI-only. Only Connect_Click submits.
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        string submittedAddress;
        if (_rawAddressMode)
        {
            submittedAddress = RawIpBox.Text.Trim();
            if (!IPAddress.TryParse(submittedAddress, out _))
            {
                WizardStatusText.Text = "Enter a valid IP address.";
                RawIpBox.Focus();
                return;
            }
        }
        else
        {
            if (!TryNormalizeIpv4Octets(Octets.Select(b => b.Text).ToArray(), out submittedAddress))
            {
                WizardStatusText.Text = "Enter all four parts of the IP address (0–255).";
                return;
            }
        }

        if (!int.TryParse(MmsPortText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var port) || port is <= 0 or > 65535)
        {
            WizardStatusText.Text =
                "Port must be between 1 and 65535.";
            return;
        }

        RelayIpAddress = submittedAddress;
        MmsPort = port;
        if (_rememberDiscoveryEndpoint)
        {
            try
            {
                UserPreferenceStore.RecordLastDiscoveryEndpoint(RelayIpAddress, MmsPort);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Discovery address could not be saved: {ex.Message}");
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
