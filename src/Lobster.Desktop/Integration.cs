using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using static Lobster.Loc;

namespace Lobster;

/// <summary>
/// Desktop integration for Linux and macOS — all per-user, no root / admin rights.
/// Linux: launcher + "Open with", Nautilus/Nemo/Caja scripts, Dolphin service menu, autostart.
/// macOS: Finder Quick Action (Services), login item (LaunchAgent).
/// </summary>
public static class Integration
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string XdgData => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");
    private static string XdgConfig => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".config");

    /// <summary>The executable to call back (inside Lobster.app on macOS).</summary>
    public static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "lobster");

    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ================================================================ Linux

    private static string DesktopFile => Path.Combine(XdgData, "applications", "lobster.desktop");
    private static string AutostartFile => Path.Combine(XdgConfig, "autostart", "lobster.desktop");
    private static string IconFile => Path.Combine(XdgData, "icons", "hicolor", "256x256", "apps", "lobster.png");
    private static string NautilusScript => Path.Combine(XdgData, "nautilus", "scripts", "Lobster");
    private static string CajaScript => Path.Combine(XdgConfig, "caja", "scripts", "Lobster");
    private static string NemoAction => Path.Combine(XdgData, "nemo", "actions", "lobster.nemo_action");
    private static string DolphinKf6 => Path.Combine(XdgData, "kio", "servicemenus", "lobster.desktop");
    private static string DolphinKf5 => Path.Combine(XdgData, "kservices5", "ServiceMenus", "lobster.desktop");

    private const string Mimes =
        "image/png;image/jpeg;image/webp;image/avif;image/heic;image/gif;image/bmp;image/tiff;image/svg+xml;" +
        "video/mp4;video/quicktime;video/x-matroska;video/webm;video/x-msvideo;video/mpeg;" +
        "audio/mpeg;audio/mp4;audio/x-wav;audio/flac;audio/ogg;audio/opus;application/pdf;" +
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document;application/msword;" +
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;application/vnd.ms-excel;" +
        "application/vnd.openxmlformats-officedocument.presentationml.presentation;application/vnd.oasis.opendocument.text;" +
        "text/markdown;text/plain;";

    public static bool LinuxIntegrated => File.Exists(DesktopFile) && File.ReadAllText(DesktopFile).Contains(Exe);

    public static void InstallLinux()
    {
        WriteIcon();
        Write(DesktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=Lobster
            GenericName={L("Convertisseur de fichiers", "File converter")}
            Comment={L("Convertir et retoucher des fichiers par glisser-déposer", "Convert and edit files by drag and drop")}
            Exec={Q(Exe)} %F
            Icon=lobster
            Terminal=false
            Categories=Utility;Graphics;AudioVideo;
            MimeType={Mimes}
            StartupNotify=false
            """);

        var script = $"""
            #!/bin/sh
            # Lobster — {L("convertir les fichiers sélectionnés", "convert the selected files")}
            IFS='
            '
            if [ -n "$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS" ]; then set -- $NAUTILUS_SCRIPT_SELECTED_FILE_PATHS; fi
            if [ -n "$CAJA_SCRIPT_SELECTED_FILE_PATHS" ]; then set -- $CAJA_SCRIPT_SELECTED_FILE_PATHS; fi
            exec {Q(Exe)} "$@"
            """;
        Write(NautilusScript, script, executable: true);
        Write(CajaScript, script, executable: true);

        Write(NemoAction, $"""
            [Nemo Action]
            Name=Lobster…
            Comment={L("Convertir avec Lobster", "Convert with Lobster")}
            Exec={Q(Exe)} %F
            Icon-Name=lobster
            Selection=notnone
            Extensions=any;
            """);

        var dolphin = $"""
            [Desktop Entry]
            Type=Service
            MimeType=all/allfiles;
            X-KDE-ServiceTypes=KonqPopupMenu/Plugin
            Actions=lobster

            [Desktop Action lobster]
            Name=Lobster…
            Icon=lobster
            Exec={Q(Exe)} %F
            """;
        Write(DolphinKf6, dolphin, executable: true);
        Write(DolphinKf5, dolphin);

        TryRun("update-desktop-database", Path.GetDirectoryName(DesktopFile)!);
        TryRun("gtk-update-icon-cache", "-q", "-t", Path.Combine(XdgData, "icons", "hicolor"));
    }

    public static void RemoveLinux()
    {
        foreach (var f in new[] { DesktopFile, NautilusScript, CajaScript, NemoAction, DolphinKf6, DolphinKf5, IconFile, AutostartFile })
            OutputPaths.TryDelete(f);
    }

    // ================================================================ macOS

    private static string LaunchAgent => Path.Combine(Home, "Library", "LaunchAgents", "tech.freia.lobster.plist");
    private static string QuickAction => Path.Combine(Home, "Library", "Services", "Lobster.workflow");

    public static bool MacIntegrated => Directory.Exists(QuickAction);

    public static void InstallMac()
    {
        var contents = Path.Combine(QuickAction, "Contents");
        Write(Path.Combine(contents, "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>NSServices</key>
              <array>
                <dict>
                  <key>NSMenuItem</key><dict><key>default</key><string>Lobster</string></dict>
                  <key>NSMessage</key><string>runWorkflowAsService</string>
                  <key>NSRequiredContext</key><dict><key>NSApplicationIdentifier</key><string>com.apple.finder</string></dict>
                  <key>NSSendFileTypes</key><array><string>public.item</string></array>
                </dict>
              </array>
            </dict>
            </plist>
            """);
        var cmd = System.Security.SecurityElement.Escape($"{Q(Exe)} \"$@\" >/dev/null 2>&1 &");
        Write(Path.Combine(contents, "document.wflow"), $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>AMApplicationBuild</key><string>523</string>
              <key>AMApplicationVersion</key><string>2.10</string>
              <key>AMDocumentVersion</key><string>2</string>
              <key>actions</key>
              <array>
                <dict>
                  <key>action</key>
                  <dict>
                    <key>AMAccepts</key><dict><key>Container</key><string>List</string><key>Optional</key><true/><key>Types</key><array><string>com.apple.cocoa.path</string></array></dict>
                    <key>AMActionVersion</key><string>2.0.3</string>
                    <key>AMApplication</key><array><string>Automator</string></array>
                    <key>AMParameterProperties</key><dict><key>COMMAND_STRING</key><dict/><key>CheckedForUserDefaultShell</key><dict/><key>inputMethod</key><dict/><key>shell</key><dict/><key>source</key><dict/></dict>
                    <key>AMProvides</key><dict><key>Container</key><string>List</string><key>Types</key><array><string>com.apple.cocoa.string</string></array></dict>
                    <key>ActionBundlePath</key><string>/System/Library/Automator/Run Shell Script.action</string>
                    <key>ActionName</key><string>Run Shell Script</string>
                    <key>ActionParameters</key>
                    <dict>
                      <key>COMMAND_STRING</key><string>{{cmd}}</string>
                      <key>CheckedForUserDefaultShell</key><true/>
                      <key>inputMethod</key><integer>1</integer>
                      <key>shell</key><string>/bin/zsh</string>
                      <key>source</key><string></string>
                    </dict>
                    <key>BundleIdentifier</key><string>com.apple.RunShellScript</string>
                    <key>CFBundleVersion</key><string>2.0.3</string>
                    <key>CanShowSelectedItemsWhenRun</key><false/>
                    <key>CanShowWhenRun</key><true/>
                    <key>Category</key><array><string>AMCategoryUtilities</string></array>
                    <key>Class Name</key><string>RunShellScriptAction</string>
                    <key>InputUUID</key><string>6A1D5E8B-1F2C-4B6E-9C3A-7E1B2D3C4F50</string>
                    <key>Keywords</key><array><string>Shell</string><string>Script</string></array>
                    <key>OutputUUID</key><string>8B2E6F9C-2A3D-4C7F-8D4B-9F2C3E4D5A61</string>
                    <key>UUID</key><string>9C3F7A0D-3B4E-4D80-9E5C-A03D4F5E6B72</string>
                    <key>UnlocalizedApplications</key><array><string>Automator</string></array>
                    <key>arguments</key><dict/>
                    <key>isViewVisible</key><integer>1</integer>
                    <key>location</key><string>309.5:253</string>
                  </dict>
                  <key>isViewVisible</key><integer>1</integer>
                </dict>
              </array>
              <key>connectors</key><dict/>
              <key>workflowMetaData</key>
              <dict>
                <key>inputTypeIdentifier</key><string>com.apple.Automator.fileSystemObject</string>
                <key>outputTypeIdentifier</key><string>com.apple.Automator.nothing</string>
                <key>presentationMode</key><integer>15</integer>
                <key>processesInput</key><false/>
                <key>serviceInputTypeIdentifier</key><string>com.apple.Automator.fileSystemObject</string>
                <key>serviceOutputTypeIdentifier</key><string>com.apple.Automator.nothing</string>
                <key>serviceProcessesInput</key><false/>
                <key>systemImageName</key><string>NSActionTemplate</string>
                <key>useAutomaticInputType</key><false/>
                <key>workflowTypeIdentifier</key><string>com.apple.Automator.servicesMenu</string>
              </dict>
            </dict>
            </plist>
            """);
        TryRun("/System/Library/CoreServices/pbs", "-update");
    }

    public static void RemoveMac()
    {
        OutputPaths.TryDelete(QuickAction);
        StartAtLogin = false;
    }

    // ================================================================ start at login (both)

    public static bool StartAtLogin
    {
        get => OperatingSystem.IsMacOS() ? File.Exists(LaunchAgent) : File.Exists(AutostartFile);
        set
        {
            if (OperatingSystem.IsMacOS())
            {
                if (!value) { OutputPaths.TryDelete(LaunchAgent); return; }
                Write(LaunchAgent, $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                      <key>Label</key><string>tech.freia.lobster</string>
                      <key>ProgramArguments</key><array><string>{System.Security.SecurityElement.Escape(Exe)}</string><string>--background</string></array>
                      <key>RunAtLoad</key><true/>
                      <key>ProcessType</key><string>Interactive</string>
                    </dict>
                    </plist>
                    """);
                return;
            }
            if (!value) { OutputPaths.TryDelete(AutostartFile); return; }
            WriteIcon();
            Write(AutostartFile, $"""
                [Desktop Entry]
                Type=Application
                Name=Lobster
                Exec={Q(Exe)} --background
                Icon=lobster
                X-GNOME-Autostart-enabled=true
                NoDisplay=true
                """);
        }
    }

    // ================================================================ helpers

    private static void WriteIcon()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconFile)!);
            using var src = AssetLoader.Open(new Uri("avares://lobster/Assets/lobster.png"));
            using var dst = File.Create(IconFile);
            src.CopyTo(dst);
        }
        catch (Exception ex) { Log.Error("icon: " + ex.Message); }
    }

    private static void Write(string path, string content, bool executable = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n") + "\n");
        if (executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static void TryRun(string exe, params string[] args)
    {
        try
        {
            var path = Path.IsPathRooted(exe) ? exe : Tools.FromPath(exe);
            if (path == null || !File.Exists(path)) return;
            var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
    }
}
