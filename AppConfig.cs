using System.Text.Json;

namespace Moneyboard;

public sealed class SoundEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public Keys Hotkey { get; set; } = Keys.None;
}

public sealed class AppConfig
{
    public List<SoundEntry> Sounds { get; set; } = new();
    public string? OutputDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public string? MicDeviceId { get; set; }
    public int SoundVolume { get; set; } = 80;
    public int MicVolume { get; set; } = 100;
    public Keys StopHotkey { get; set; } = Keys.None;

    static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moneyboard");
    static readonly string FilePath = System.IO.Path.Combine(Dir, "config.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch { /* corrupt config: start fresh */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
