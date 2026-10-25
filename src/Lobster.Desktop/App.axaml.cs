using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using static Lobster.Loc;

namespace Lobster;

public partial class App : Application
{
    public static new App Current => (App)Application.Current!;

    private RadialMenuWindow _menu = null!;
    private BubbleWindow? _bubble;
    private JobsWindow _jobs = null!;
    private SettingsWindow? _settings;
    private FileBatcher _batcher = null!;
    private TrayIcon? _tray;
    private IGestureMonitor? _gesture;
    private string? _notifyIcon;

    /// <summary>A window always available for pickers and the clipboard.</summary>
    public TopLevel? Helper => _menu;
    public string? GestureName => _gesture?.Name;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppState.Host = new AvaloniaHost();
        AppState.Jobs = new JobManager(AppState.Settings.MaxParallelJobs);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("UI: " + e.Exception);
            e.Handled = true;
            _ = Dialogs.Error("Lobster", e.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("domain: " + e.ExceptionObject);

        _menu = new RadialMenuWindow();
        _jobs = new JobsWindow(AppState.Jobs);
        _batcher = new FileBatcher(files => _menu.ShowClickMode(files));

        AppState.Jobs.Changed += () => Ui.Post(() => { if (AppState.Jobs.AnyActive) _jobs.Reveal(); });
        AppState.Jobs.BatchFinished += batch => Ui.Post(() => OnBatchFinished(batch));

        SetupTray();
        SetupGesture();
        RefreshBubble();

        // macOS: files opened with Lobster (Finder "Open With", Dock icon drop) arrive as activation events.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs f)
                    _batcher.Add(f.Files.Select(i => i.TryGetLocalPath()).Where(p => p != null).Select(p => p!));
            };
        }

        SingleInstance.Listen(lines => Dispatcher.UIThread.Post(() => HandleArgs(lines)));

        var args = desktop.Args ?? Array.Empty<string>();
        if (!AppState.Settings.FirstRunDone) FirstRun();
        else if (!args.Contains("--background") && args.All(a => a.StartsWith("--")))
            Notify(L("Lobster est prêt", "Lobster is ready"), HowTo);
        HandleArgs(args);

        base.OnFrameworkInitializationCompleted();
    }

    private string HowTo => _gesture != null
        ? L("Glissez des fichiers en maintenant Maj, ou déposez-les sur la bulle.", "Drag files while holding Shift, or drop them on the bubble.")
        : L("Déposez des fichiers sur la bulle Lobster.", "Drop files on the Lobster bubble.");

    private void FirstRun()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Integration.InstallMac();
            else Integration.InstallLinux();
            Integration.StartAtLogin = true;
        }
        catch (Exception ex) { Log.Error("first run: " + ex); }
        AppState.Settings.FirstRunDone = true;
        AppState.Settings.Save();
        OpenSettings(welcome: true);
    }

    // ================================================================ gesture & bubble

    private void SetupGesture()
    {
        _gesture = GestureMonitors.Create();
        if (_gesture == null) return;
        _gesture.Triggered += (x, y) => Dispatcher.UIThread.Post(() => { if (Kit.ModalDepth == 0) _menu.ArmForDrag(new PixelPoint(x, y)); });
        _gesture.Released += () => Dispatcher.UIThread.Post(_menu.OnDragReleased);
        _gesture.Pressed += (x, y) => Dispatcher.UIThread.Post(() => _menu.OnGlobalMouseDown(new PixelPoint(x, y)));
        if (_gesture.Start()) return;
        Log.Info($"gesture monitor {_gesture.Name} unavailable");
        if (_gesture is MacGestureMonitor)
        {
            // macOS asks for "Input Monitoring" once; keep trying for a few minutes while the user answers.
            int tries = 0;
            var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            retry.Tick += (_, _) =>
            {
                if (_gesture == null || ++tries > 60) { retry.Stop(); return; }
                if (_gesture.Start()) { retry.Stop(); Log.Info("gesture monitor started"); }
            };
            retry.Start();
            return;
        }
        _gesture.Dispose();
        _gesture = null;
    }

    public void RefreshBubble()
    {
        if (!AppState.Settings.ShowBubble)
        {
            _bubble?.Hide();
            return;
        }
        if (_bubble == null)
        {
            _bubble = new BubbleWindow();
            _bubble.FilesApproaching += at => { if (Kit.ModalDepth == 0) _menu.ArmForDrag(at); };
            _bubble.FilesDropped += files => _menu.ShowClickMode(files);
            _bubble.Clicked += OpenFiles;
            _bubble.ShowAtSavedPosition();
        }
        else
        {
            if (!_bubble.IsVisible) _bubble.ShowAtSavedPosition();
            (_bubble.Content as Control)?.InvalidateVisual();
        }
    }

    public void PreviewMenu() => _menu.Preview();

    // ================================================================ entry points

    private void HandleArgs(IEnumerable<string> args)
    {
        var list = args.ToList();
        if (list.Contains("--quit")) { Quit(); return; }
        var files = list.Where(a => !a.StartsWith("--") && !a.StartsWith("-psn_")).ToList();
        if (files.Count > 0) _batcher.Add(files);
        else if (list.Contains("--show")) OpenSettings();
    }

    public void OpenSettings(bool welcome = false)
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); return; }
        Track(new SettingsWindow(welcome));
    }

    /// <summary>Language switch: replace the settings window at the same place and size.</summary>
    public void ReopenSettings(SettingsWindow old)
    {
        Track(new SettingsWindow { Width = old.Width, Height = old.Height, Position = old.Position, WindowStartupLocation = WindowStartupLocation.Manual });
        old.Close();
    }

    private void Track(SettingsWindow w)
    {
        _settings = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_settings, w)) _settings = null; };
        w.Show();
        w.Activate();
    }

    private async void OpenFiles()
    {
        var files = await _menu.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Fichiers à traiter avec Lobster", "Files to process with Lobster"),
            AllowMultiple = true
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        if (paths.Count > 0) _menu.ShowClickMode(paths);
    }

    public void ShowJobs() => _jobs.Reveal();

    // ================================================================ tray / menu bar

    private void SetupTray()
    {
        try
        {
            _tray = new TrayIcon { ToolTipText = "Lobster", Icon = Kit.Icon, IsVisible = true };
            BuildTrayMenu();
            _tray.Clicked += (_, _) => ShowJobs();
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
        }
        catch (Exception ex) { Log.Error("tray: " + ex.Message); }
    }

    private void BuildTrayMenu()
    {
        if (_tray == null) return;
        NativeMenuItem Item(string text, Action a)
        {
            var i = new NativeMenuItem(text);
            i.Click += (_, _) => a();
            return i;
        }
        var menu = new NativeMenu();
        menu.Add(Item(L("Ouvrir des fichiers…", "Open files…"), OpenFiles));
        menu.Add(Item(L("Activité", "Activity"), ShowJobs));
        menu.Add(new NativeMenuItemSeparator());
        var bubble = new NativeMenuItem(L("Bulle Lobster", "Lobster bubble")) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = AppState.Settings.ShowBubble };
        bubble.Click += (_, _) =>
        {
            AppState.Settings.ShowBubble = !AppState.Settings.ShowBubble;
            AppState.Settings.Save();
            bubble.IsChecked = AppState.Settings.ShowBubble;
            RefreshBubble();
        };
        menu.Add(bubble);
        menu.Add(Item(L("Paramètres…", "Settings…"), () => OpenSettings()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(L("Quitter Lobster", "Quit Lobster"), Quit));
        _tray.Menu = menu;
    }

    public void RefreshTray() => BuildTrayMenu();

    // ================================================================ notifications

    public void Notify(string title, string text, string? revealPath = null)
    {
        if (!AppState.Settings.Notifications) return;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
                Process.Start("osascript", new[] { "-e", $"display notification \"{Esc(text)}\" with title \"{Esc(title)}\"" });
                return;
            }
            var notify = Tools.FromPath("notify-send");
            if (notify == null) return;
            _notifyIcon ??= SaveNotifyIcon();
            var args = new List<string> { "-a", "Lobster", "-t", "3500" };
            if (_notifyIcon != null) { args.Add("-i"); args.Add(_notifyIcon); }
            args.Add(title); args.Add(text);
            Process.Start(notify, args);
        }
        catch (Exception ex) { Log.Error("notify: " + ex.Message); }
    }

    private static string? SaveNotifyIcon()
    {
        try
        {
            var path = Path.Combine(Settings.AppDataDir, "lobster.png");
            if (!File.Exists(path))
            {
                using var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://lobster/Assets/lobster.png"));
                using var dst = File.Create(path);
                src.CopyTo(dst);
            }
            return path;
        }
        catch { return null; }
    }

    private void OnBatchFinished(IReadOnlyList<Job> batch)
    {
        int ok = batch.Count(j => j.State == JobState.Done);
        int failed = batch.Count(j => j.State == JobState.Failed);
        if (ok + failed == 0) return;
        var output = batch.Where(j => j.State == JobState.Done).SelectMany(j => j.Outputs).FirstOrDefault();
        string title = failed == 0
            ? (ok == 1 ? L("C'est prêt", "Done") : L($"{ok} fichiers prêts", $"{ok} files ready"))
            : L($"{failed} échec{(failed > 1 ? "s" : "")}", $"{failed} failed");
        string text = failed == 0
            ? (output != null ? Path.GetFileName(output.TrimEnd('/')) : L("Terminé", "Done"))
            : (ok > 0 ? L($"{ok} réussi(s). ", $"{ok} succeeded. ") : "") + L("Détails dans la fenêtre d'activité.", "Details in the activity window.");
        Notify(title, text, output);
        if (failed > 0) ShowJobs();
    }

    public void Quit()
    {
        AppState.Settings.Save();
        try { _gesture?.Dispose(); } catch { }
        foreach (var j in AppState.Jobs.All.Where(j => j.IsActive).ToList()) j.Cts.Cancel();
        try { Directory.Delete(Path.Combine(Path.GetTempPath(), "Lobster"), true); } catch { }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
}
