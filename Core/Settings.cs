using System.Text.Json;

namespace UsbCopyStation.Core;

internal sealed class Settings
{
    public string SourceFolder { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool UseFolder { get; set; } = true;
    public string FileSystem { get; set; } = "NTFS";
    public string Label { get; set; } = "USB";
    public string WorkDir { get; set; } = Path.Combine(AppDir, "work");
    public bool AutoMode { get; set; }
    public bool Verify { get; set; } = true;
    public bool UniqueSerial { get; set; } = true;
    public bool AllowOverwriteData { get; set; }
    public bool EjectWhenDone { get; set; }
    public int MaxSizeGb { get; set; } = 256;
    public int MaxParallel { get; set; } = 16;
    /// <summary>Fizički port (LocationPath) → broj slota.</summary>
    public Dictionary<string, int> SlotMap { get; set; } = new();

    public static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsbCopyStation");

    private static string FilePath => Path.Combine(AppDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* pokvaren fajl → podrazumevana podešavanja */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }
}
