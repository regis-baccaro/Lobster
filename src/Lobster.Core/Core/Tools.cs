using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lobster;

/// <summary>Finds the external engines Lobster can use. Everything runs locally.</summary>
public static class Tools
{
    private static readonly Dictionary<string, string?> Cache = new();

    public static void Reset()
    {
        lock (Cache) Cache.Clear();
    }

    private static string? Find(string key, Func<IEnumerable<string?>> candidates)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
            string? found = null;
            try { found = candidates().FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c)); }
            catch (Exception ex) { Log.Error($"tool scan {key}: {ex.Message}"); }
            Cache[key] = found;
            return found;
        }
    }

    public static bool IsWindows => OperatingSystem.IsWindows();
    private static string Exe(string name) => IsWindows ? name + ".exe" : name;

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // ---------------------------------------------------------------- ffmpeg

    public static string? Ffmpeg => Find("ffmpeg", () =>
    {
        var list = new List<string?>
        {
            AppState.Settings.FfmpegPath,
            Path.Combine(Settings.AppDataDir, "tools", Exe("ffmpeg")),
            Path.Combine(AppContext.BaseDirectory, Exe("ffmpeg")),
            FromPath(Exe("ffmpeg")),
        };
        if (IsWindows)
        {
            list.Add(Path.Combine(LocalAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
            list.Add(@"C:\ProgramData\chocolatey\bin\ffmpeg.exe");
            list.Add(Path.Combine(Home, "scoop", "shims", "ffmpeg.exe"));
            list.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
            list.Add(Path.Combine(ProgramFiles, "ffmpeg", "bin", "ffmpeg.exe"));
            list.AddRange(Glob(Path.Combine(LocalAppData, "Microsoft", "WinGet", "Packages"), "Gyan.FFmpeg*", "ffmpeg.exe"));
            list.AddRange(Glob(Path.Combine(LocalAppData, "Microsoft", "WinGet", "Packages"), "BtbN.FFmpeg*", "ffmpeg.exe"));
        }
        else
        {
            list.AddRange(new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg", "/var/lib/flatpak/exports/bin/ffmpeg",
                Path.Combine(Home, ".local", "bin", "ffmpeg") });
        }
        return list;
    });

    public static string? Ffprobe
    {
        get
        {
            var ff = Ffmpeg;
            if (ff == null) return null;
            var probe = Path.Combine(Path.GetDirectoryName(ff)!, Exe("ffprobe"));
            return File.Exists(probe) ? probe : FromPath(Exe("ffprobe"));
        }
    }

    // ---------------------------------------------------------------- PDF helpers

    public static string? Ghostscript => Find("gs", () =>
    {
        if (!IsWindows) return new[] { FromPath("gs"), "/usr/bin/gs", "/usr/local/bin/gs" };
        var list = new List<string?> { FromPath("gswin64c.exe") };
        list.AddRange(Glob(Path.Combine(ProgramFiles, "gs"), "gs*", "gswin64c.exe").OrderByDescending(x => x));
        return list;
    });

    /// <summary>Poppler's renderer (Linux): sharp, fast PDF → image.</summary>
    public static string? Pdftoppm => Find("pdftoppm", () => new[] { FromPath(Exe("pdftoppm")), "/usr/bin/pdftoppm", "/usr/local/bin/pdftoppm" });

    // ---------------------------------------------------------------- office

    public static string? LibreOffice => Find("soffice", () => IsWindows
        ? new[]
        {
            Path.Combine(ProgramFiles, "LibreOffice", "program", "soffice.exe"),
            Path.Combine(ProgramFilesX86, "LibreOffice", "program", "soffice.exe"),
            FromPath("soffice.exe"),
        }
        : new[]
        {
            FromPath("soffice"), FromPath("libreoffice"), "/usr/bin/soffice", "/usr/bin/libreoffice",
            "/usr/lib/libreoffice/program/soffice", "/opt/libreoffice/program/soffice", "/snap/bin/libreoffice",
        });

    public static bool HasComApp(string progId)
    {
        if (!IsWindows) return false;
        try { return Type.GetTypeFromProgID(progId) != null; }
        catch { return false; }
    }

    public static bool HasWord => HasComApp("Word.Application");
    public static bool HasExcel => HasComApp("Excel.Application");
    public static bool HasPowerPoint => HasComApp("PowerPoint.Application");
    public static bool HasMsOffice => HasWord || HasExcel || HasPowerPoint;

    // ---------------------------------------------------------------- helpers

    public static string? FromPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> Glob(string root, string dirPattern, string exe)
    {
        if (!Directory.Exists(root)) yield break;
        string[] dirs;
        try { dirs = Directory.GetDirectories(root, dirPattern); } catch { yield break; }
        foreach (var d in dirs)
        {
            string[] found;
            try { found = Directory.GetFiles(d, exe, SearchOption.AllDirectories); } catch { continue; }
            foreach (var f in found) yield return f;
        }
    }

    /// <summary>Package-manager command that installs a tool on this Linux distribution.</summary>
    public static string LinuxInstallCommand(string debName, string rpmName, string archName)
    {
        if (FromPath("apt") != null) return $"sudo apt install {debName}";
        if (FromPath("dnf") != null) return $"sudo dnf install {rpmName}";
        if (FromPath("pacman") != null) return $"sudo pacman -S {archName}";
        if (FromPath("zypper") != null) return $"sudo zypper install {rpmName}";
        return debName;
    }
}

public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string StdOut, string StdErrTail);

    public static async Task<Result> Run(string exe, IEnumerable<string> args, CancellationToken ct,
        Action<string>? onStdOutLine = null, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var errTail = new LinkedList<string>();

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (onStdOutLine != null) onStdOutLine(e.Data);
            else lock (stdout) stdout.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (errTail)
            {
                errTail.AddLast(e.Data);
                if (errTail.Count > 60) errTail.RemoveFirst();
            }
        };

        if (!p.Start()) throw new InvalidOperationException(Loc.L($"Impossible de lancer {Path.GetFileName(exe)}", $"Cannot start {Path.GetFileName(exe)}"));
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } }))
        {
            await p.WaitForExitAsync().ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();

        string tail;
        lock (errTail) tail = string.Join("\n", errTail);
        return new Result(p.ExitCode, stdout.ToString(), tail);
    }

    /// <summary>Last meaningful lines of a tool's stderr, for user-facing errors.</summary>
    public static string Summarize(string stderr, int lines = 3)
    {
        var meaningful = stderr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("frame=") && !l.StartsWith("size="))
            .ToList();
        return string.Join(" · ", meaningful.Skip(Math.Max(0, meaningful.Count - lines)));
    }
}
