using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace IPChanger.Forms;

public partial class EditWindow : Window
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Models.IpAddressSetting? ConfiguredPreset { get; private set; }

    public EditWindow()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    public EditWindow(Models.IpAddressSetting? setting) : this()
    {
        if (setting != null)
        {
            NameBox.Text = setting.Name;
            IpBox.Text = setting.IpAddress;
            MaskBox.Text = setting.SubnetMask;
            GatewayBox.Text = setting.DefaultGateway;
            PrimaryDnsBox.Text = setting.PrimaryDns;
            SecondaryDnsBox.Text = setting.SecondaryDns;
        }
    }

    private void ApplyLocalization()
    {
        Title = L.T("FormEditTitle");
        LabelName.Text = L.T("LabelName");
        LabelIp.Text = L.T("LabelIp");
        LabelMask.Text = L.T("LabelMask");
        LabelGateway.Text = L.T("LabelGateway");
        LabelPrimaryDns.Text = L.T("LabelPrimaryDns");
        LabelSecondaryDns.Text = L.T("LabelSecondaryDns");
        OkButton.Content = L.T("BtnOk");
        CancelButton.Content = L.T("BtnCancel");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!IsValidIp(IpBox.Text.Trim(), out var ipError))
        {
            ShowWarning(this, ipError);
            return;
        }

        if (!IsValidIp(MaskBox.Text.Trim(), out var maskError))
        {
            ShowWarning(this, maskError);
            return;
        }

        if (!string.IsNullOrWhiteSpace(GatewayBox.Text)
            && !IsValidIp(GatewayBox.Text.Trim(), out var gwError))
        {
            ShowWarning(this, gwError);
            return;
        }

        if (!string.IsNullOrWhiteSpace(PrimaryDnsBox.Text)
            && !IsValidIp(PrimaryDnsBox.Text.Trim(), out var dns1Error))
        {
            ShowWarning(this, dns1Error);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SecondaryDnsBox.Text)
            && !IsValidIp(SecondaryDnsBox.Text.Trim(), out var dns2Error))
        {
            ShowWarning(this, dns2Error);
            return;
        }

        ConfiguredPreset = new Models.IpAddressSetting
        {
            Name           = NameBox.Text.Trim(),
            IpAddress      = IpBox.Text.Trim(),
            SubnetMask     = MaskBox.Text.Trim(),
            DefaultGateway = GatewayBox.Text.Trim(),
            PrimaryDns     = PrimaryDnsBox.Text.Trim(),
            SecondaryDns   = SecondaryDnsBox.Text.Trim()
        };
        DialogResult = true;
        Close();
    }

    private static void ShowWarning(Window owner, string message)
    {
        MessageBox.Show(owner, message, L.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static bool IsValidIp(string ip, out string error)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            error = "IP address is required.";
            return false;
        }

        if (!IPAddress.TryParse(ip, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = $"Invalid IP address: {ip}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}