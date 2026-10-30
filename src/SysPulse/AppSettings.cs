using System.Globalization;
using System.Text.Json;

namespace SysPulse;

/// <summary>
/// Tiny persisted key/value store (%LOCALAPPDATA%\SysPulse\settings.json).
/// Failures are ignored — settings are never essential.
/// </summary>
internal static class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysPulse", "settings.json");

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _values;

    private static Dictionary<string, string> Values
    {
        get
        {
            if (_values is not null)
                return _values;
            try
            {
                if (File.Exists(FilePath))
                    _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
            return _values ??= [];
        }
    }

    public static string? Get(string key)
    {
        lock (Gate)
            return Values.TryGetValue(key, out var v) ? v : null;
    }

    public static bool GetBool(string key, bool fallback) => Get(key) is { } v ? v == "1" : fallback;

    public static int? GetInt(string key) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    public static void Set(string key, bool value) => Set(key, value ? "1" : "0");

    public static void Set(string key, int? value) => Set(key, value?.ToString(CultureInfo.InvariantCulture));

    public static void Set(string key, string? value) => SetMany((key, value));

    /// <summary>Sets several values with a single write of the file. A null value removes the key.</summary>
    public static void SetMany(params (string Key, string? Value)[] items)
    {
        lock (Gate)
        {
            foreach (var (key, value) in items)
            {
                if (value is null)
                    Values.Remove(key);
                else
                    Values[key] = value;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Values, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
