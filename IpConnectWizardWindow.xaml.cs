using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class IpConnectWizardWindow : Window
{
    public static readonly DependencyProperty RelayIpAddressProperty = DependencyProperty.Register(
        nameof(RelayIpAddress), typeof(string), typeof(IpConnectWizardWindow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MmsPortTextProperty = DependencyProperty.Register(
        nameof(MmsPortText), typeof(string), typeof(IpConnectWizardWindow), new PropertyMetadata("102"));

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
    public int MmsPort { get; private set; } = 102;
    public ObservableCollection<string> RecentRelayIps { get; } = new();

    public IpConnectWizardWindow(string initialIp = "", int initialPort = 102,
        bool preferLastDiscoveryEndpoint = false)
    {
        _rememberDiscoveryEndpoint = preferLastDiscoveryEndpoint;
        InitializeComponent();
        foreach (var endpoint in UserPreferenceStore.LoadRecentEndpoints())
            RecentRelayIps.Add(endpoint);

        // This dialog is reused from several workspace actions. The last
        // explicitly confirmed address, not a selected card or hardcoded NIC,
        // is the primary suggestion for the next IP Discovery action.
        if (_rememberDiscoveryEndpoint &&
            UserPreferenceStore.TryLoadLastDiscoveryEndpoint(out var lastIp, out var lastPort))
        {
            RelayIpAddress = lastIp;
            MmsPortText = lastPort.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            RelayIpAddress = initialIp?.Trim() ?? string.Empty;
            MmsPortText = (initialPort is > 0 and <= 65535 ? initialPort : 102)
                .ToString(CultureInfo.InvariantCulture);
        }
        Loaded += (_, _) => RelayIpBox.Focus();
    }

    // WPF's editable ComboBox displays text in a templated TextBox, not directly
    // in its selection presenter. Apply alignment to both modes while preserving
    // the theme template, keyboard editing, focus and recent-address binding.
    private void RelayIpBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (RelayIpBox.Template.FindName("PART_EditableTextBox", RelayIpBox) is TextBox editor)
        {
            editor.TextAlignment = TextAlignment.Center;
            editor.HorizontalContentAlignment = HorizontalAlignment.Center;
            editor.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        // Editable ComboBox commits its binding on LostFocus. Enter can submit
        // while the editor still owns focus: read the live text to avoid stale IPs.
        RelayIpAddress = RelayIpBox.Text.Trim();
        if (!System.Net.IPAddress.TryParse(RelayIpAddress, out _))
        {
            WizardStatusText.Text = "Enter a valid IPv4 or IPv6 address.";
            RelayIpBox.Focus();
            return;
        }

        if (!int.TryParse(MmsPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is <= 0 or > 65535)
        {
            WizardStatusText.Text = "MMS port must be between 1 and 65535. Standard IEC 61850 MMS uses TCP 102.";
            return;
        }

        MmsPort = port;

        // Persist the operator's valid *submission*, not only a successful
        // MMS association. Cancel/invalid inputs never overwrite the last IP.
        if (_rememberDiscoveryEndpoint)
        {
            try
            {
                UserPreferenceStore.RecordLastDiscoveryEndpoint(RelayIpAddress, MmsPort);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // Preference storage must never prevent an intentional connection.
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
