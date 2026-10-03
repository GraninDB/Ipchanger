using System.Windows;

namespace IPChanger.Forms;

public partial class AboutWindow : Window
{
    public static readonly DependencyProperty VersionTextProperty =
        DependencyProperty.Register(
            nameof(VersionText),
            typeof(string),
            typeof(AboutWindow),
            new PropertyMetadata(string.Empty));

    public string VersionText
    {
        get => (string)GetValue(VersionTextProperty);
        set => SetValue(VersionTextProperty, value);
    }

    public AboutWindow()
    {
        InitializeComponent();

        Title = L.T("AboutTitle");
        AppNameText.Text = L.T("AppTitle");
        DescriptionText.Text = L.T("AboutDescription");
        CloseButton.Content = L.T("BtnClose");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
