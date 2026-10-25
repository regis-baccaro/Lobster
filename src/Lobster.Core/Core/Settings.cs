using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lobster;

public enum ModifierKey { Shift, Ctrl, Alt }
public enum GestureScope { ExplorerAndDesktop, Everywhere }
public enum OutputMode { SameFolder, Subfolder, Custom }

public sealed class Settings
{
    public bool GestureEnabled { get; set; } = true;
    public ModifierKey Modifier { get; set; } = ModifierKey.Shift;
    public GestureScope Scope { get; set; } = GestureScope.ExplorerAndDesktop;
    public OutputMode Output { get; set; } = OutputMode.SameFolder;
    public string? CustomFolder { get; set; }
    public string? FfmpegPath { get; set; }
    public bool Notifications { get; set; } = true;
    public int MaxParallelJobs { get; set; } = 2;
    public bool FirstRunDone { get; set; }
    public Language Language { get; set; } = Loc.SystemDefault;
    public string Palette { get; set; } = "lobster";
    public string? CustomAccent { get; set; } = "#E8452C";
    /// <summary>Linux / macOS: floating drop bubble.</summary>
    public bool ShowBubble { get; set; } = true;
    public int? BubbleX { get; set; }
    public int? BubbleY { get; set; }

    public static string AppDataDir
    {
        get
        {
            // Windows: %LOCALAPPDATA%\Lobster · Linux: ~/.local/share/Lobster · macOS: ~/Library/Application Support/Lobster
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Lobster");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string FilePath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception ex) { Log.Error("settings load: " + ex.Message); }
        return new Settings();
    }

    private System.Threading.Timer? _saveTimer;

    /// <summary>Debounced save (for values that change continuously, like the bubble position).</summary>
    public void SaveSoon()
    {
        _saveTimer ??= new System.Threading.Timer(_ => Save());
        _saveTimer.Change(800, System.Threading.Timeout.Infinite);
    }

    private static readonly object SaveGate = new();

    public void Save()
    {
        try { lock (SaveGate) File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { Log.Error("settings save: " + ex.Message); }
    }
}
