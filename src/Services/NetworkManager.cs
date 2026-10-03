using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using IPChanger;
using IPChanger.Models;

namespace IPChanger.Services;

public class NetworkManager
{
    private const int ProcessTimeoutMs = 20000;

    private static HashSet<string>? GetNcpaConnectionIds()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementBaseObject baseObj in searcher.Get())
            {
                using var obj = (ManagementObject)baseObj;
                var id = obj["NetConnectionID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    set.Add(id.Trim());
            }
            return set;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? GetFriendlyNamesByDescription()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description, NetConnectionID FROM Win32_NetworkAdapter " +
                "WHERE NetConnectionID IS NOT NULL AND (Name IS NOT NULL OR Description IS NOT NULL)");
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementBaseObject baseObj in searcher.Get())
            {
                using var obj = (ManagementObject)baseObj;
                var name = obj["Name"]?.ToString();
                var description = obj["Description"]?.ToString();
                var netConnectionId = obj["NetConnectionID"]?.ToString();
                if (string.IsNullOrEmpty(netConnectionId))
                    continue;
                if (!string.IsNullOrEmpty(name))
                    dict[name.Trim()] = netConnectionId;
                if (!string.IsNullOrEmpty(description))
                    dict[description.Trim()] = netConnectionId;
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, bool>? GetDhcpStatusByDescription()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Description, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementBaseObject baseObj in searcher.Get())
            {
                using var obj = (ManagementObject)baseObj;
                var description = obj["Description"]?.ToString();
                var dhcp = obj["DHCPEnabled"];
                if (string.IsNullOrEmpty(description) || dhcp == null)
                    continue;
                try
                {
                    dict[description.Trim()] = Convert.ToBoolean(dhcp);
                }
                catch { }
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static NetworkInterfaceInfo? TryBuildInterfaceInfo(
        NetworkInterface iface,
        Dictionary<string, string>? friendlyNames,
        Dictionary<string, bool>? dhcpStatus,
        bool requireIpv4)
    {
        var desc = (iface.Description ?? string.Empty).Trim();

        var props = iface.GetIPProperties();
        var uni = props.UnicastAddresses
            .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);

        if (requireIpv4 && uni == null)
            return null;

        var dnsAddresses = props.DnsAddresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .ToList();

        var displayName = friendlyNames != null && friendlyNames.TryGetValue(desc, out var fn)
            ? fn
            : iface.Name;

        return new NetworkInterfaceInfo
        {
            Name = iface.Name,
            Description = displayName,
            OriginalDescription = desc,
            MacAddress = iface.GetPhysicalAddress().ToString(),
            InterfaceType = iface.NetworkInterfaceType.ToString(),
            CurrentIpAddress = uni?.Address.ToString() ?? string.Empty,
            CurrentSubnetMask = uni?.IPv4Mask?.ToString() ?? string.Empty,
            DefaultGateway = props.GatewayAddresses
                .FirstOrDefault(g => g.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address?.ToString() ?? string.Empty,
            PrimaryDns = dnsAddresses.ElementAtOrDefault(0) ?? string.Empty,
            SecondaryDns = dnsAddresses.ElementAtOrDefault(1) ?? string.Empty,
            IsDhcpEnabled = dhcpStatus != null
                && dhcpStatus.TryGetValue(desc, out var dhcpEnabled)
                && dhcpEnabled,
            IsActive = iface.OperationalStatus == OperationalStatus.Up
        };
    }

    private static List<NetworkInterfaceInfo> CollectInterfaces(bool requireIpv4)
    {
        var result = new List<NetworkInterfaceInfo>();
        var ncpaIds = GetNcpaConnectionIds();
        var friendlyNames = GetFriendlyNamesByDescription();
        var dhcpStatus = GetDhcpStatusByDescription();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            if (ncpaIds != null)
            {
                var connectionName = (iface.Name ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(connectionName) || !ncpaIds.Contains(connectionName))
                    continue;
            }

            var info = TryBuildInterfaceInfo(iface, friendlyNames, dhcpStatus, requireIpv4);
            if (info != null)
                result.Add(info);
        }

        return result;
    }

    public static List<NetworkInterfaceInfo> GetNetworkInterfaces()
        => CollectInterfaces(requireIpv4: true);

    public static List<NetworkInterfaceInfo> GetAllNetworkInterfaces()
        => CollectInterfaces(requireIpv4: false);

    public static string? GetFriendlyName(string description)
    {
        var dict = GetFriendlyNamesByDescription();
        var key = description?.Trim();
        if (dict != null && !string.IsNullOrEmpty(key) && dict.TryGetValue(key, out var name))
            return name;
        return null;
    }

    // ─────────────────────────── Public operations ───────────────────────────

    public static async Task<(bool success, string message)> ApplyIpAddressAsync(string interfaceName, IpAddressSetting setting, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return (false, L.T("MsgEmptyInterfaceName"));
        if (!IsValidIPv4(setting.IpAddress))
            return (false, L.T("MsgInvalidIpAddress"));
        if (!IsValidIPv4(setting.SubnetMask))
            return (false, L.T("MsgInvalidSubnetMask"));
        if (!string.IsNullOrWhiteSpace(setting.DefaultGateway) && !IsValidIPv4(setting.DefaultGateway))
            return (false, L.T("MsgInvalidGateway"));

        var prefix = SubnetMaskToPrefixLength(setting.SubnetMask);
        if (prefix == null)
            return (false, L.T("MsgInvalidSubnetMask"));

        progress?.Report(L.T("StepClearingIpRoutes"));
        var staticResult = await RunSetStaticIpAsync(interfaceName, setting.IpAddress, prefix.Value, setting.DefaultGateway, progress).ConfigureAwait(false);
        if (!staticResult.success)
            return staticResult;

        if (!string.IsNullOrWhiteSpace(setting.PrimaryDns))
        {
            if (!IsValidIPv4(setting.PrimaryDns))
                return (false, L.T("MsgInvalidPrimaryDns"));
            if (!string.IsNullOrWhiteSpace(setting.SecondaryDns) && !IsValidIPv4(setting.SecondaryDns))
                return (false, L.T("MsgInvalidSecondaryDns"));

            progress?.Report(L.T("StepSettingDns"));
            var dnsResult = await RunSetDnsAsync(interfaceName, setting.PrimaryDns, setting.SecondaryDns, progress).ConfigureAwait(false);
            if (!dnsResult.success)
                return dnsResult;
        }
        else
        {
            progress?.Report(L.T("StepResettingDnsAuto"));
            var dnsResetResult = await RunResetDnsAsync(interfaceName, progress).ConfigureAwait(false);
            if (!dnsResetResult.success)
                return dnsResetResult;
        }

        return (true, L.T("MsgApplySuccess"));
    }

    public static async Task<(bool success, string message)> SetDhcpAsync(string interfaceName, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return (false, L.T("MsgEmptyInterfaceName"));
        return await RunSetDhcpAsync(interfaceName, progress).ConfigureAwait(false);
    }

    public static async Task<(bool success, string message)> SetDhcpDnsAsync(string interfaceName, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return (false, L.T("MsgEmptyInterfaceName"));
        return await RunResetDnsAsync(interfaceName, progress).ConfigureAwait(false);
    }

    public static async Task<(bool success, string message)> SetDnsAsync(string interfaceName, string primaryDns, string secondaryDns, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return (false, L.T("MsgEmptyInterfaceName"));
        if (!IsValidIPv4(primaryDns))
            return (false, L.T("MsgInvalidPrimaryDns"));
        if (!string.IsNullOrWhiteSpace(secondaryDns) && !IsValidIPv4(secondaryDns))
            return (false, L.T("MsgInvalidSecondaryDns"));

        return await RunSetDnsAsync(interfaceName, primaryDns, secondaryDns, progress).ConfigureAwait(false);
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static bool IsValidIPv4(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return System.Net.IPAddress.TryParse(value, out var addr)
            && addr.AddressFamily == AddressFamily.InterNetwork;
    }

    private static int? SubnetMaskToPrefixLength(string mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
            return null;
        if (!System.Net.IPAddress.TryParse(mask, out var ip))
            return null;
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var bytes = ip.GetAddressBytes();
        int prefix = 0;
        bool zeroSeen = false;

        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                bool bit = (b & (1 << i)) != 0;
                if (bit)
                {
                    if (zeroSeen)
                        return null;
                    prefix++;
                }
                else
                {
                    zeroSeen = true;
                }
            }
        }

        return prefix;
    }

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''");

    // ─────────────────────────── PowerShell scripts ───────────────────────────

    /// <summary>
    /// Включает DHCP для IP и DNS.
    /// Существующие команды (Set-NetIPInterface -Dhcp Enabled и
    /// Set-DnsClientServerAddress -ResetServerAddresses) не тронуты —
    /// они уже работают. Добавлена только очистка статического маршрута
    /// по умолчанию (0.0.0.0/0), который иначе остаётся в таблице маршрутизации
    /// и отображается как «Основной шлюз», даже когда режим уже DHCP.
    /// Также удаляем оставшиеся статические IP-адреса — без этого диалог
    /// свойств TCP/IPv4 в Windows показывает «Use the following IP address»
    /// с пустыми полями вместо «Obtain an IP address automatically».
    /// Принудительно выставляем EnableDHCP = 1 в реестре — аналогично
    /// RunSetStaticIpAsync, который выставляет EnableDHCP = 0.
    /// </summary>
    private static async Task<(bool success, string message)> RunSetDhcpAsync(string interfaceName, IProgress<string>? progress = null)
    {
        var alias = EscapePowerShellString(interfaceName);
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    # Удаляем оставшиеся статические IP-адреса до включения DHCP.
    # Иначе Set-NetIPInterface -Dhcp Enabled может не сработать корректно,
    # и диалог TCP/IPv4 покажет «Use the following IP address» с пустыми полями.
    Remove-NetIPAddress -InterfaceAlias '{alias}' -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue

    Set-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily IPv4 -Dhcp Enabled
    Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ResetServerAddresses

     # Принудительно выставляем EnableDHCP = 1 в реестре для этого интерфейса.
     # Без этого PersistentStore может продолжать считать, что там статический IP,
     # и диалог свойств TCP/IPv4 не переключается на «Получить IP-адрес автоматически».
     $adapter = Get-NetAdapter -InterfaceAlias '{alias}'
     $guid = $adapter.InterfaceGuid
     $regPath = ""HKLM:\SYSTEM\CurrentControlSet\services\Tcpip\Parameters\Interfaces\$guid""
     Set-ItemProperty -Path $regPath -Name EnableDHCP -Value 1 -Type DWord

     # Пауза, чтобы реестр и NetTCPIP успели применить изменения.
     Start-Sleep -Seconds 2

     # Дополнительно: удаляем статический шлюз (маршрут по умолчанию 0.0.0.0/0).
    # Он живёт отдельно от режима DHCP и сам не сбрасывается.
    # Если DHCP-сервер выдаёт шлюз, DHCP-клиент установит маршрут заново из аренды.
    Get-NetRoute -InterfaceAlias '{alias}' -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
}} catch {{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}}
exit 0
";
        progress?.Report(L.T("StepResettingDns"));
        var result = await ExecutePowerShellAsync(script).ConfigureAwait(false);
        if (!result.success)
            return result;

        // Принудительно запускаем DHCP-клиент. Ошибка /renew не фатальна —
        // если линка нет, DHCP-клиент подхватит адрес позже, когда появится связь.
        progress?.Report(L.T("StepRenewingDhcp"));
        await ExecuteProcessAsync("ipconfig", $"/renew \"{interfaceName}\"").ConfigureAwait(false);

        return (true, L.T("MsgDhcpEnabled"));
    }

    /// <summary>
    /// Устанавливает статический IPv4-адрес, шлюз, маску.
    /// Перед New-NetIPAddress принудительно выставляем EnableDHCP = 0 в реестре
    /// для этого интерфейса — иначе PersistentStore продолжает считать, что там DHCP,
    /// и New-NetIPAddress падает с "Inconsistent parameters PolicyStore PersistentStore and Dhcp Enabled".
    /// </summary>
    private static async Task<(bool success, string message)> RunSetStaticIpAsync(
        string interfaceName,
        string ipAddress,
        int prefixLength,
        string defaultGateway,
        IProgress<string>? progress = null)
    {
        var alias = EscapePowerShellString(interfaceName);
        var ip = EscapePowerShellString(ipAddress);

        var gatewayPart = string.IsNullOrWhiteSpace(defaultGateway)
            ? string.Empty
            : $" -DefaultGateway '{EscapePowerShellString(defaultGateway)}'";

        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    # 1. Получаем GUID интерфейса и сбрасываем EnableDHCP в реестре.
    #    Это ключевой шаг — без него New-NetIPAddress падает на PersistentStore.
    $adapter = Get-NetAdapter -InterfaceAlias '{alias}'
    $guid = $adapter.InterfaceGuid
    $regPath = ""HKLM:\SYSTEM\CurrentControlSet\services\Tcpip\Parameters\Interfaces\$guid""
    Set-ItemProperty -Path $regPath -Name EnableDHCP -Value 0 -Type DWord

    # 2. Убираем старые IP-адреса и маршруты.
    Remove-NetIPAddress -InterfaceAlias '{alias}' -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue
    Remove-NetRoute -InterfaceAlias '{alias}' -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue

    # 3. Небольшая пауза, чтобы изменения в реестре успели примениться.
    Start-Sleep -Seconds 2

    # 4. Ставим статику. Теперь конфликта с PersistentStore нет.
    New-NetIPAddress -InterfaceAlias '{alias}' -IPAddress '{ip}' -PrefixLength {prefixLength}{gatewayPart}
}} catch {{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}}
exit 0
";
        progress?.Report(L.T("StepSettingStaticIp"));
        var result = await ExecutePowerShellAsync(script).ConfigureAwait(false);
        return result;
    }

    private static async Task<(bool success, string message)> RunSetDnsAsync(
        string interfaceName,
        string primaryDns,
        string secondaryDns,
        IProgress<string>? progress = null)
    {
        var alias = EscapePowerShellString(interfaceName);
        var addresses = new List<string> { primaryDns };
        if (!string.IsNullOrWhiteSpace(secondaryDns))
            addresses.Add(secondaryDns);

        var arrayLiteral = "@(" + string.Join(",", addresses.Select(a => $"'{EscapePowerShellString(a)}'")) + ")";

        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ServerAddresses {arrayLiteral}
}} catch {{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}}
exit 0
";
        progress?.Report(L.T("StepSettingDns"));
        var result = await ExecutePowerShellAsync(script).ConfigureAwait(false);
        return result;
    }

    private static async Task<(bool success, string message)> RunResetDnsAsync(string interfaceName, IProgress<string>? progress = null)
    {
        var alias = EscapePowerShellString(interfaceName);
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ResetServerAddresses
}} catch {{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}}
exit 0
";
return await ExecutePowerShellAsync(script).ConfigureAwait(false);
}

    // ─────────────────────────── Process runners ───────────────────────────

    private static async Task<(bool success, string message)> ExecutePowerShellAsync(string script)
    {
        var bytes = Encoding.Unicode.GetBytes(script);
        var encoded = Convert.ToBase64String(bytes);
        return await ExecuteProcessAsync("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded).ConfigureAwait(false);
    }

    private static async Task<(bool success, string message)> ExecuteProcessAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, string.Format(L.T("MsgProcessStartFailed"), fileName));

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            bool exited = await Task.Run(() => process.WaitForExit(ProcessTimeoutMs)).ConfigureAwait(false);
            if (!exited && !process.HasExited)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                return (false, string.Format(L.T("MsgProcessTimeout"), fileName));
            }

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
                return (true, L.T("MsgApplySuccess"));

            var errorDetails = string.IsNullOrWhiteSpace(error) ? output : error;
            if (string.IsNullOrWhiteSpace(errorDetails))
                errorDetails = string.Format(L.T("MsgExitCode"), process.ExitCode);

            return (false, string.Format(L.T("MsgProcessError"), errorDetails.Trim()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, string.Format(L.T("MsgProcessError"), ex.Message));
        }
    }
}

public record NetworkInterfaceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OriginalDescription { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public string InterfaceType { get; init; } = string.Empty;
    public string CurrentIpAddress { get; init; } = string.Empty;
    public string CurrentSubnetMask { get; init; } = string.Empty;
    public string DefaultGateway { get; init; } = string.Empty;
    public string PrimaryDns { get; init; } = string.Empty;
    public string SecondaryDns { get; init; } = string.Empty;
    public bool IsDhcpEnabled { get; init; }
    public bool IsActive { get; init; }
}