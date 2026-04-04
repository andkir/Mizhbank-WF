using System.Text.Json;

namespace MizhbankNotifier.Services;

/// <summary>
/// Reads and writes rate snapshots to JSON files in the "Data" folder
/// next to the executable. Files are overwritten on every update.
/// </summary>
public static class RatePersistence
{
    public static readonly string DataDir =
        Path.Combine(AppContext.BaseDirectory, "Data");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = false,
    };

    public static T? Load<T>(string fileName)
    {
        var path = Path.Combine(DataDir, fileName);
        if (!File.Exists(path)) return default;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, _opts);
        }
        catch { return default; }
    }

    public static void Save<T>(string fileName, T value)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var path = Path.Combine(DataDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(value, _opts));
        }
        catch { /* best-effort */ }
    }
}
