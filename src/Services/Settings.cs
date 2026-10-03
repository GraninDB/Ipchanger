using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace IPChanger.Services;

internal sealed class Settings
{
    private const string FileName = "settings.json";

    private static readonly UTF8Encoding Utf8WithBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static string AppDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IPChanger", "config");

    private static string SettingsPath => Path.Combine(AppDir, FileName);

    public static Settings Instance { get; } = new();

    private readonly Dictionary<string, string>? _data;

    private Settings()
    {
        _data = LoadFromFile();
    }

    public string? GetString(string key)
    {
        if (_data != null && _data.TryGetValue(key, out var value))
            return value;
        return null;
    }

    public void SetString(string key, string value)
    {
        if (_data == null)
            return;

        _data[key] = value;
        SaveToFile(_data);
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        if (_data != null && _data.TryGetValue(key, out var value))
            return bool.TryParse(value, out var result) ? result : defaultValue;
        return defaultValue;
    }

    public void SetBool(string key, bool value)
    {
        SetString(key, value.ToString());
    }

    private static Dictionary<string, string>? LoadFromFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new Dictionary<string, string>();

            var json = File.ReadAllText(SettingsPath, Utf8WithBom);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            };
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options) ?? new Dictionary<string, string>();
        }
        catch
        {
            return null;
        }
    }

    private static void SaveToFile(Dictionary<string, string> data)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(SettingsPath, json, Utf8WithBom);
        }
        catch
        {
            // Ignore settings save errors.
        }
    }
}