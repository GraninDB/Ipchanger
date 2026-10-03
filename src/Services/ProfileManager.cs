using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using IPChanger.Models;

namespace IPChanger.Services;

public class ProfileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly string _profilesPath;
    private readonly object _lock = new();

    private List<NetworkProfile> _profiles = new();

    public ProfileManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir  = Path.Combine(appData, "IPChanger", "config");
        if (!Directory.Exists(appDir))
            Directory.CreateDirectory(appDir);

        _profilesPath = Path.Combine(appDir, "profiles.json");

        // Загружаем ровно один раз — в конструкторе.
        LoadProfilesFromDisk();
    }

    // ---------- Загрузка / сохранение на диск ----------

    private void LoadProfilesFromDisk()
    {
        if (!File.Exists(_profilesPath))
        {
            _profiles = new();
            return;
        }

        try
        {
            var json = File.ReadAllText(_profilesPath, Utf8WithBom);
            _profiles = JsonSerializer.Deserialize<List<NetworkProfile>>(json, JsonOptions) ?? new();
        }
        catch
        {
            _profiles = new();
        }
    }

    private void SaveProfiles()
    {
        var dir = Path.GetDirectoryName(_profilesPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_profiles, JsonOptions);
        File.WriteAllText(_profilesPath, json, Utf8WithBom);
    }

    // ---------- Профили ----------

    /// <summary>Возвращает глубокую копию списка профилей.</summary>
    public List<NetworkProfile> GetAllProfiles()
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_profiles, JsonOptions);
            return JsonSerializer.Deserialize<List<NetworkProfile>>(json, JsonOptions) ?? new();
        }
    }

    /// <summary>Возвращает глубокую копию профиля, чтобы вызывающий код не мог случайно испортить состояние.</summary>
    public NetworkProfile? GetProfile(string interfaceName)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null) return null;

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            return JsonSerializer.Deserialize<NetworkProfile>(json, JsonOptions);
        }
    }

    public void EnsureProfileExists(string interfaceName, string description, string macAddress)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null)
            {
                _profiles.Add(new NetworkProfile
                {
                    InterfaceName        = interfaceName,
                    InterfaceDescription = description,
                    MacAddress           = macAddress
                });
            }
            else
            {
                // Replace with updated record (immutable)
                var updated = profile with
                {
                    InterfaceDescription = description,
                    MacAddress           = macAddress
                };
                var idx = _profiles.FindIndex(p => p.InterfaceName == interfaceName);
                if (idx >= 0) _profiles[idx] = updated;
            }

            SaveProfiles();
        }
    }

    public void AddPreset(string interfaceName, IpAddressSetting preset)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null)
            {
                profile = new NetworkProfile { InterfaceName = interfaceName };
                _profiles.Add(profile);
            }

            var presetToAdd = preset;
            if (string.IsNullOrEmpty(preset.Name))
                presetToAdd = preset with { Name = $"Preset {profile.Presets.Count + 1}" };

            profile.Presets.Add(presetToAdd);
            SaveProfiles();
        }
    }

    /// <summary>
    /// Заменяет пресет по индексу. Возвращает false, если профиль или индекс не найдены.
    /// </summary>
    public bool UpdatePreset(string interfaceName, int index, IpAddressSetting preset)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null || index < 0 || index >= profile.Presets.Count)
                return false;

            profile.Presets[index] = preset;
            SaveProfiles();
            return true;
        }
    }

    public bool DeletePreset(string interfaceName, int index)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null || index < 0 || index >= profile.Presets.Count)
                return false;

            profile.Presets.RemoveAt(index);
            SaveProfiles();
            return true;
        }
    }

    public void DeleteAllPresets(string interfaceName)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.InterfaceName == interfaceName);
            if (profile == null) return;

            profile.Presets.Clear();
            SaveProfiles();
        }
    }
}