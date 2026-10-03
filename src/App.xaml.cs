using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

using Application    = System.Windows.Application;
using MessageBox     = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

using IPChanger.Services;

namespace IPChanger;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\IPChanger-SingleInstance";

    private Mutex? _instanceMutex;
    private TrayIconManager? _tray;
    private ProfileManager? _profileManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Регистрируем провайдер кодовых страниц, чтобы Encoding.GetEncoding(OEM)
        // работал для OEM-кодировок (866, 437 и т.д.), используемых ipconfig.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 1. Elevate if not admin (IPChanger has no requireAdministrator manifest)
        if (!IsAdministrator())
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(psi);
            }
            catch (Exception)
            {
                MessageBox.Show(L.T("MsgAdminRequired"), L.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        // 2. Language (match StartAsAdmin: before mutex)
        var savedCulture = Settings.Instance.GetString("ui_culture");
        if (!string.IsNullOrEmpty(savedCulture))
        {
            try
            {
                L.SetCulture(CultureInfo.GetCultureInfo(savedCulture));
            }
            catch
            {
                L.SetCulture(CultureInfo.CurrentUICulture);
            }
        }
        else
        {
            L.SetCulture(CultureInfo.CurrentUICulture);
        }

        _instanceMutex = new Mutex(
            initiallyOwned: true,
            name: SingleInstanceMutexName,
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;

            MessageBox.Show(
                L.T("MsgAlreadyRunning"),
                L.T("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 4. Create tray
        _profileManager = new ProfileManager();
        _tray = new TrayIconManager(_profileManager);
        _tray.ExitRequested += () => Shutdown();

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), L.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Мьютекс не был захвачен этим потоком — игнорируем.
            }
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}