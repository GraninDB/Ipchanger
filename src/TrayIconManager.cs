using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using IPChanger.Models;
using IPChanger.Services;
using IPChanger.Forms;

namespace IPChanger;

internal sealed class TrayIconManager : IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly Settings _settings = Settings.Instance;
    private readonly Icon _networkIcon;
    private readonly Image _checkmarkIcon;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private bool _disposed;

    public TrayIconManager(ProfileManager profileManager)
    {
        _profileManager = profileManager;
        _networkIcon = CreateNetworkIcon();
        _checkmarkIcon = CreateCheckmarkIcon();

        InitializeTray();
    }

    public event Action? ExitRequested;

    private void InitializeTray()
    {
        _contextMenu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon
        {
            Icon = _networkIcon,
            Text = L.T("AppTitle"),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) =>
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow?.IsVisible == true)
            {
                mainWindow.Activate();
            }
            else
            {
                RebuildMenu();
                _contextMenu?.Show(Cursor.Position);
            }
        };

        _contextMenu.Opening += (s, e) => RebuildMenu();
        NetworkChange.NetworkAddressChanged += (s, e) => RebuildMenu();
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            RebuildMenuCore();
        else
            dispatcher.BeginInvoke(new Action(RebuildMenuCore));
    }

    private void RebuildMenuCore()
    {
        if (_contextMenu == null || _profileManager == null)
            return;

        _contextMenu.SuspendLayout();

        var oldItems = _contextMenu.Items.Cast<ToolStripItem>().ToArray();
        _contextMenu.Items.Clear();
        foreach (var old in oldItems)
            old.Dispose();

        var interfaces = NetworkManager.GetAllNetworkInterfaces();
        var sortedInterfaces = interfaces
            .OrderBy(i => i.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var iface in sortedInterfaces)
        {
            _profileManager.EnsureProfileExists(iface.Name, iface.OriginalDescription, iface.MacAddress);

            var menuText = !string.IsNullOrEmpty(iface.OriginalDescription) &&
                           !string.Equals(iface.Description, iface.OriginalDescription, StringComparison.OrdinalIgnoreCase)
                ? $"{iface.Description} [{iface.OriginalDescription}]"
                : iface.Description;

            var ifaceMenu = new ToolStripMenuItem
            {
                Text = $"{menuText} ({iface.CurrentIpAddress})",
                Image = iface.IsActive ? _checkmarkIcon : null
            };

            var profile = _profileManager.GetProfile(iface.Name);

            if (profile?.Presets.Count > 0)
            {
                foreach (var (preset, index) in profile.Presets.Select((p, i) => (p, i)))
                {
                    var presetMenu = new ToolStripMenuItem
                    {
                        Text = preset.ToString(),
                        Tag = preset
                    };

                    var applyItem = new ToolStripMenuItem(L.T("MenuApply"));
                    applyItem.Click += (s, e) => ApplyPreset(iface.Name, preset);

                    var editItem = new ToolStripMenuItem(L.T("MenuEdit"));
                    editItem.Click += (s, e) => EditPreset(iface.Name, preset, index);

                    var deleteItem = new ToolStripMenuItem(L.T("MenuDelete"));
                    deleteItem.Click += (s, e) => DeletePreset(iface.Name, index);

                    presetMenu.DropDownItems.AddRange(new ToolStripItem[] { applyItem, editItem, deleteItem });
                    ifaceMenu.DropDownItems.Add(presetMenu);
                }
            }
            else
            {
                var noPresets = new ToolStripMenuItem
                {
                    Text = L.T("MenuNoPresets"),
                    Enabled = false
                };
                ifaceMenu.DropDownItems.Add(noPresets);
            }

            ifaceMenu.DropDownItems.Add(new ToolStripSeparator());

            var addPresetItem = new ToolStripMenuItem(L.T("MenuAddPreset"));
            addPresetItem.Click += (s, e) => AddPreset(iface.Name);
            ifaceMenu.DropDownItems.Add(addPresetItem);

            var saveCurrentItem = new ToolStripMenuItem(L.T("MenuSaveCurrentAsPreset"));
            saveCurrentItem.Click += (s, e) => SaveCurrentAsPreset(iface.Name);
            ifaceMenu.DropDownItems.Add(saveCurrentItem);

            var setDhcpItem = new ToolStripMenuItem(L.T("MenuSetDhcp"));
            setDhcpItem.Click += (s, e) => SetDhcp(iface.Name);
            ifaceMenu.DropDownItems.Add(setDhcpItem);

            var propertiesItem = new ToolStripMenuItem(L.T("MenuInterfaceProperties"));
            propertiesItem.Click += (s, e) => OpenInterfaceProperties(iface.Name);
            ifaceMenu.DropDownItems.Add(propertiesItem);

            _contextMenu.Items.Add(ifaceMenu);
        }

        _contextMenu.Items.Add(new ToolStripSeparator());

        var ipconfigItem = new ToolStripMenuItem(L.T("MenuIpconfigAll"));
        ipconfigItem.Click += (s, e) => ShowIpconfig();
        _contextMenu.Items.Add(ipconfigItem);

        var networkConnectionsItem = new ToolStripMenuItem(L.T("MenuNetworkConnections"));
        networkConnectionsItem.Click += (s, e) => OpenNetworkConnections();
        _contextMenu.Items.Add(networkConnectionsItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var languageMenu = new ToolStripMenuItem("Language / Язык");
        var currentCulture = L.CurrentCulture;

        try
        {
            var translationsDir = Path.Combine(AppContext.BaseDirectory, "Translations");
            if (Directory.Exists(translationsDir))
            {
                foreach (var file in Directory.GetFiles(translationsDir, "*.json").OrderBy(f => f))
                {
                    var cultureCode = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(cultureCode))
                        continue;

                    var isCurrent = cultureCode.Equals(currentCulture, StringComparison.OrdinalIgnoreCase);
                    var cultureName = GetCultureDisplayName(cultureCode);

                    var langItem = new ToolStripMenuItem(cultureName)
                    {
                        Checked = isCurrent,
                        Tag = cultureCode
                    };
                    langItem.Click += (s, e) => ChangeLanguage(cultureCode);
                    languageMenu.DropDownItems.Add(langItem);
                }
            }
        }
        catch
        {
            // Игнорируем ошибки чтения переводов.
        }

        _contextMenu.Items.Add(languageMenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem(L.T("MenuAbout"));
        aboutItem.Click += (s, e) => ShowAbout();
        _contextMenu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem(L.T("MenuExit"));
        exitItem.Click += (s, e) => ExitRequested?.Invoke();
        _contextMenu.Items.Add(exitItem);

        _contextMenu.ResumeLayout();
    }

    private static string GetCultureDisplayName(string cultureCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureCode).NativeName;
        }
        catch
        {
            return cultureCode;
        }
    }

    private async void ApplyPreset(string interfaceName, Models.IpAddressSetting preset)
    {
        await ShowApplyStatusAsync(progress => NetworkManager.ApplyIpAddressAsync(interfaceName, preset, progress));
    }

    private async void SetDhcp(string interfaceName)
    {
        await ShowApplyStatusAsync(progress => NetworkManager.SetDhcpAsync(interfaceName, progress));
    }

    private async Task ShowApplyStatusAsync(Func<IProgress<string>, Task<(bool success, string message)>> applyAction)
    {
        var statusWindow = new ApplyStatusWindow();
        statusWindow.ClearSteps();

        var progress = new Progress<string>(step => statusWindow.AddStep(step));

        statusWindow.Show();

        var (success, message) = await applyAction(progress);

        statusWindow.AddStep(success ? L.T("MsgApplySuccess") : string.Format(L.T("MsgProcessError"), message));

        statusWindow.Activate();
    }

    private void EditPreset(string interfaceName, Models.IpAddressSetting preset, int index)
    {
        var form = new EditWindow(preset);
        if (form.ShowDialog() == true && form.ConfiguredPreset != null)
        {
            if (!_profileManager.UpdatePreset(interfaceName, index, form.ConfiguredPreset))
            {
                DialogHelper.ShowWarning(System.Windows.Application.Current.MainWindow, string.Format(L.T("MsgCannotGetSettings"), interfaceName, index), L.T("AppTitle"));
            }
        }
        RebuildMenu();
    }

    private void AddPreset(string interfaceName)
    {
        var form = new EditWindow(null);
        if (form.ShowDialog() == true && form.ConfiguredPreset != null)
        {
            _profileManager.AddPreset(interfaceName, form.ConfiguredPreset);
        }
        RebuildMenu();
    }

    private void SaveCurrentAsPreset(string interfaceName)
    {
        var interfaces = NetworkManager.GetNetworkInterfaces();
        var iface = interfaces.FirstOrDefault(i => i.Name == interfaceName);

        if (iface == null)
        {
            DialogHelper.ShowWarning(System.Windows.Application.Current.MainWindow, L.T("MsgCannotGetSettings"), L.T("MsgCannotGetSettingsTitle"));
            return;
        }

        var preset = new Models.IpAddressSetting
        {
            IpAddress = iface.CurrentIpAddress,
            SubnetMask = iface.CurrentSubnetMask,
            DefaultGateway = iface.DefaultGateway,
            PrimaryDns = iface.PrimaryDns,
            SecondaryDns = iface.SecondaryDns
        };

        _profileManager.AddPreset(interfaceName, preset);
        RebuildMenu();
    }

    private void DeletePreset(string interfaceName, int index)
    {
        var profile = _profileManager.GetProfile(interfaceName);
        if (profile == null || index < 0 || index >= profile.Presets.Count)
            return;

        var preset = profile.Presets[index];

        if (!DialogHelper.ShowQuestion(System.Windows.Application.Current.MainWindow, string.Format(L.T("MsgDeleteConfirm"), preset.Name), L.T("MsgDeleteConfirmTitle")))
        {
            return;
        }

        if (!_profileManager.DeletePreset(interfaceName, index))
        {
            DialogHelper.ShowWarning(System.Windows.Application.Current.MainWindow, string.Format(L.T("MsgCannotGetSettings"), interfaceName, index), L.T("AppTitle"));
        }
        RebuildMenu();
    }

    private void ShowAbout()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.3.1.0";

        var form = new AboutWindow
        {
            VersionText = string.Format(L.T("VersionText"), version)
        };
        form.ShowDialog();
    }

    private void ShowIpconfig()
    {
        var window = new IpconfigWindow();
        window.ShowDialog();
    }

    private void OpenNetworkConnections()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ncpa.cpl",
                UseShellExecute = true
            });
        }
        catch
        {
            // Игнорируем — ncpa.cpl может быть недоступен.
        }
    }

    private void OpenInterfaceProperties(string interfaceName)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var sta = new Thread(() => OpenInterfaceProperties(interfaceName));
            sta.SetApartmentState(ApartmentState.STA);
            sta.IsBackground = true;
            sta.Start();
            sta.Join();
            return;
        }

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                throw new InvalidOperationException("Shell.Application COM object not found.");

            dynamic? shell = null;
            dynamic? networkConnections = null;
            dynamic? items = null;

            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    throw new InvalidOperationException("Failed to create Shell.Application instance.");

                networkConnections = shell.Namespace(0x31);
                if (networkConnections == null)
                    throw new InvalidOperationException("Network Connections folder not found.");

                items = networkConnections.Items();
                int count = items.Count;

                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    dynamic? item = null;
                    try
                    {
                        item = items.Item(i);
                        if (string.Equals((string)item.Name, interfaceName, StringComparison.OrdinalIgnoreCase))
                        {
                            item.InvokeVerb("Properties");
                            found = true;
                            break;
                        }
                    }
                    finally
                    {
                        if (item != null)
                            Marshal.ReleaseComObject(item);
                    }
                }

                if (!found)
                    throw new InvalidOperationException($"Adapter '{interfaceName}' not found.");
            }
            finally
            {
                if (items != null)
                    Marshal.ReleaseComObject(items);
                if (networkConnections != null)
                    Marshal.ReleaseComObject(networkConnections);
                if (shell != null)
                    Marshal.ReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowWarning(System.Windows.Application.Current.MainWindow, $"{L.T("MsgInterfacePropertiesError")}\n{ex.Message}", L.T("AppTitle"));
        }
    }

    private void ChangeLanguage(string culture)
    {
        try
        {
            L.SetCulture(CultureInfo.GetCultureInfo(culture));
            _settings.SetString("ui_culture", culture);
            RebuildMenu();
        }
        catch
        {
            DialogHelper.ShowWarning(System.Windows.Application.Current.MainWindow, "Failed to change language.", L.T("AppTitle"));
        }
    }

    private static Image CreateCheckmarkIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        using (var brush = new SolidBrush(Color.FromArgb(0, 192, 0)))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillPolygon(brush, new[]
            {
                new PointF(3, 9),
                new PointF(7, 13),
                new PointF(13, 4)
            });
        }
        return bmp;
    }

    private static Icon CreateNetworkIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                    return icon;
            }
        }
        catch
        {
            // Игнорируем — используем системную иконку.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_contextMenu != null)
        {
            var items = _contextMenu.Items.Cast<ToolStripItem>().ToArray();
            foreach (var item in items)
                item.Dispose();
            _contextMenu.Dispose();
            _contextMenu = null;
        }

        NetworkChange.NetworkAddressChanged -= (s, e) => RebuildMenu();

        _disposed = true;
    }
}