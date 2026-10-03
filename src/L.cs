using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace IPChanger;

internal static class L
{
    private static CultureInfo _culture = CultureInfo.CurrentUICulture;
    private static Dictionary<string, string>? _external;
    private static Dictionary<string, string>? _enFallback;

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void SetCulture(CultureInfo culture)
    {
        _culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        _external = LoadExternal(culture.Name);
        _enFallback ??= LoadExternal("en");
    }

    public static string T(string key)
    {
        if (_external != null && _external.TryGetValue(key, out var externalValue))
            return externalValue;

        if (_enFallback != null && _enFallback.TryGetValue(key, out var enValue))
            return enValue;

        return key;
    }

    public static string CurrentCulture => _culture.Name;

    private static Dictionary<string, string>? LoadExternal(string culture)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "Translations", culture + ".json");
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path, Utf8WithBom);
            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = false
            };
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options) ?? new(StringComparer.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    static L()
    {
        _enFallback = LoadExternal("en");
    }
}