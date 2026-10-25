using System;
using System.IO;

namespace Lobster;

/// <summary>
/// Picks where results go and never overwrites anything: every output name is
/// reserved (created empty) before an engine writes to it.
/// </summary>
public static class OutputPaths
{
    private static readonly object Gate = new();

    public static string FallbackDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lobster");

    public static string DirFor(string source)
    {
        var srcDir = Path.GetDirectoryName(Path.GetFullPath(source.TrimEnd('\\', '/')))
                     ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var s = AppState.Settings;
        string dir = s.Output switch
        {
            OutputMode.Subfolder => Path.Combine(srcDir, "Lobster"),
            OutputMode.Custom when !string.IsNullOrWhiteSpace(s.CustomFolder) => s.CustomFolder!,
            _ => srcDir
        };
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { dir = FallbackDir; Directory.CreateDirectory(dir); }
        return dir;
    }

    /// <summary>Reserve "name (suffix).ext" next to the source.</summary>
    public static string Reserve(string source, string ext, string? suffix = null)
    {
        var baseName = Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(suffix)) baseName += $" ({suffix})";
        try { return ReserveIn(DirFor(source), baseName, ext); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Read-only source (DVD, network share, protected folder): fall back to the Desktop.
            return ReserveIn(FallbackDir, baseName, ext);
        }
    }

    public static string ReserveIn(string dir, string baseName, string ext)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            baseName = Sanitize(baseName);
            for (int i = 1; ; i++)
            {
                var name = i == 1 ? baseName : $"{baseName} ({i})";
                var path = Path.Combine(dir, $"{name}.{ext}");
                if (File.Exists(path) || Directory.Exists(path)) continue;
                using (File.Create(path)) { }
                return path;
            }
        }
    }

    /// <summary>Reserve a fresh folder, e.g. for one image per PDF page.</summary>
    public static string ReserveFolder(string source, string suffix)
    {
        lock (Gate)
        {
            var dir = DirFor(source);
            var baseName = Sanitize($"{Path.GetFileNameWithoutExtension(source)} ({suffix})");
            for (int i = 1; ; i++)
            {
                var path = Path.Combine(dir, i == 1 ? baseName : $"{baseName} ({i})");
                if (Directory.Exists(path) || File.Exists(path)) continue;
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "Lobster", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        return d;
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
