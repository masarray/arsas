using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
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

    public int MmsPort { get; private set; } = 102;
    public ObservableCollection<string> RecentRelayIps { get; } = new();

    public IpConnectWizardWindow(string initialIp = "", int initialPort = 102)
    {
        InitializeComponent();
        RelayIpAddress = initialIp?.Trim() ?? string.Empty;
        MmsPortText = (initialPort <= 0 ? 102 : initialPort).ToString(CultureInfo.InvariantCulture);
        foreach (var endpoint in UserPreferenceStore.LoadRecentEndpoints())
            RecentRelayIps.Add(endpoint);
        Loaded += (_, _) => RelayIpBox.Focus();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        RelayIpAddress = (RelayIpAddress ?? string.Empty).Trim();
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
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
