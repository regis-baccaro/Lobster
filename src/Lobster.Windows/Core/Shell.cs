using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Lobster;

/// <summary>Windows integration, all per-user (no admin rights needed).</summary>
public static class Shell
{
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Lobster.exe");

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string[] VerbKeys = { @"Software\Classes\*\shell\Lobster", @"Software\Classes\Directory\shell\Lobster" };

    private static string SendToLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Lobster.lnk");
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lobster.lnk");

    public static void Reveal(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
            else Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { Log.Error("reveal: " + ex.Message); }
    }

    // ---------------------------------------------------------------- start with Windows

    public static bool StartsWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Lobster") is string s && s.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Lobster", $"\"{ExePath}\" --background");
            else k.DeleteValue("Lobster", false);
        }
    }

    // ---------------------------------------------------------------- Explorer context menu

    public static bool ContextMenuInstalled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(VerbKeys[0] + @"\command");
            return k?.GetValue("") is string s && s.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            foreach (var key in VerbKeys)
            {
                if (!value) { Registry.CurrentUser.DeleteSubKeyTree(key, false); continue; }
                using var verb = Registry.CurrentUser.CreateSubKey(key);
                verb.SetValue("MUIVerb", "Lobster…");
                verb.SetValue("Icon", $"\"{ExePath}\",0");
                verb.SetValue("MultiSelectModel", "Player");
                using var cmd = verb.CreateSubKey("command");
                cmd.SetValue("", $"\"{ExePath}\" \"%1\"");
            }
        }
    }

    // ---------------------------------------------------------------- Send To / Start menu

    public static bool SendToInstalled
    {
        get => File.Exists(SendToLink);
        set
        {
            if (value) CreateShortcut(SendToLink, Loc.L("Convertir avec Lobster", "Convert with Lobster"));
            else OutputPaths.TryDelete(SendToLink);
        }
    }

    public static void EnsureStartMenuShortcut()
    {
        try { CreateShortcut(StartMenuLink, Loc.L("Lobster — conversions par glisser-déposer", "Lobster — drag-and-drop conversions")); }
        catch (Exception ex) { Log.Error("start menu: " + ex.Message); }
    }

    private static void CreateShortcut(string linkPath, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new Exception("WScript.Shell indisponible");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = ExePath;
            link.IconLocation = ExePath + ",0";
            link.Description = description;
            link.WorkingDirectory = Path.GetDirectoryName(ExePath);
            link.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    public static void RemoveAll()
    {
        try { StartsWithWindows = false; } catch { }
        try { ContextMenuInstalled = false; } catch { }
        try { SendToInstalled = false; } catch { }
        OutputPaths.TryDelete(StartMenuLink);
    }
}
