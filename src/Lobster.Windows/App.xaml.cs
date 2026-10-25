using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Lobster;

public partial class App : Application
{
    public static Settings Settings => AppState.Settings;
    public static JobManager Jobs => AppState.Jobs;
    public static new App Current => (App)Application.Current;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private MouseHook? _hook;
    private DragGesture? _gesture;
    private RadialMenuWindow? _menu;
    private JobsWindow? _jobsWindow;
    private SettingsWindow? _settingsWindow;
    private FileBatcher? _batcher;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _gestureItem;
    private string? _lastOutput;

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lobster");

    private static bool RunningFromInstallDir => string.Equals(
        Path.GetDirectoryName(Path.GetFullPath(Shell.ExePath)), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppState.Host = new WpfHost();
        AppState.Settings = Settings.Load();
        PdfEngine.NativeRenderer = new WinPdfRenderer();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("UI: " + ex.Exception);
            ex.Handled = true;
            try { Dialog.Error("Lobster", ex.Exception.Message); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("domain: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("task: " + ex.Exception); ex.SetObserved(); };

        _mutex = new Mutex(false, @"Local\FreiaTech.Lobster");
        bool first;
        try { first = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { first = true; }
        _ownsMutex = first;
        if (!first)
        {
            // A new build launched from outside the install folder while an older Lobster runs: update it.
            if (e.Args.Length == 0 && !Shell.IsPackaged && !RunningFromInstallDir && OfferUpdate()) { Shutdown(); return; }
            SingleInstance.Send(e.Args, () => Win32.AllowSetForegroundWindow(Win32.ASFW_ANY));
            Shutdown();
            return;
        }

        Log.Info($"start {Shell.ExePath} args={string.Join(' ', e.Args)}");

        if (!Shell.IsPackaged && !RunningFromInstallDir && e.Args.Length == 0 && OfferInstall(update: File.Exists(Path.Combine(InstallDir, "Lobster.exe")))) return;

        AppState.Jobs = new JobManager(Settings.MaxParallelJobs);
        _menu = new RadialMenuWindow();
        _menu.Prepare();
        _jobsWindow = new JobsWindow(Jobs);
        _batcher = new FileBatcher(files => _menu.ShowClickMode(files));

        Jobs.Changed += () => Ui.Post(() => { if (Jobs.AnyActive) _jobsWindow.Reveal(); });
        Jobs.BatchFinished += batch => Ui.Post(() => OnBatchFinished(batch));

        SetupTray();

        _gesture = new DragGesture();
        _hook = new MouseHook();
        _gesture.Attach(_hook);
        _gesture.Triggered += (x, y) => Dispatcher.BeginInvoke(() => _menu.ArmForDrag(x, y));
        _gesture.Released += () => Dispatcher.BeginInvoke(() => _menu.OnDragReleased());
        _gesture.Pressed += (x, y) => Dispatcher.BeginInvoke(() => _menu.OnGlobalMouseDown(x, y));
        _hook.Start();

        SingleInstance.Listen(lines => Dispatcher.BeginInvoke(() => HandleArgs(lines)));

        if (!Settings.FirstRunDone) FirstRun();
        else if (e.Args.Length == 0) Notify(Loc.L("Lobster est prêt", "Lobster is ready"), HowTo);

        HandleArgs(e.Args);
    }

    private static string HowTo => Settings.Modifier switch
    {
        ModifierKey.Ctrl => Loc.L("Glissez des fichiers en maintenant Ctrl.", "Drag files while holding Ctrl."),
        ModifierKey.Alt => Loc.L("Glissez des fichiers en maintenant Alt.", "Drag files while holding Alt."),
        _ => Loc.L("Glissez des fichiers en maintenant Maj.", "Drag files while holding Shift.")
    };

    // ================================================================ install / update

    /// <summary>Another (installed) Lobster is running and this one was launched from elsewhere.</summary>
    private bool OfferUpdate()
    {
        int choice = Dialog.Show(Loc.L("Mettre à jour Lobster", "Update Lobster"),
            Loc.L("Lobster est déjà en cours d'exécution. Remplacer la version installée par celle-ci ?",
                  "Lobster is already running. Replace the installed version with this one?"),
            new[] { Loc.L("Mettre à jour", "Update"), Loc.L("Annuler", "Cancel") }, "\uE896");
        if (choice != 0) return false;

        SingleInstance.Send(new[] { "--quit" });
        // Wait for the old instance to release the mutex (it exits within a second or two).
        try
        {
            using var m = new Mutex(false, @"Local\FreiaTech.Lobster");
            bool got = false;
            try { got = m.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { got = true; }
            if (got) m.ReleaseMutex();
        }
        catch (Exception ex) { Log.Error("update wait: " + ex.Message); }
        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;

        // The old Lobster.exe stays locked until its process is fully gone: retry for a few seconds.
        Exception? last = null;
        for (int i = 0; i < 24; i++)
        {
            try { CopyToInstallDir(); last = null; break; }
            catch (Exception ex) { last = ex; Thread.Sleep(250); }
        }
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(InstallDir, "Lobster.exe")) { UseShellExecute = false, WorkingDirectory = InstallDir });
        }
        catch (Exception ex) { last ??= ex; }
        if (last != null)
        {
            Log.Error("update: " + last);
            Dialog.Error(Loc.L("Mise à jour", "Update"), Loc.L("Copie impossible : ", "Could not copy: ") + last.Message);
        }
        return true;
    }

    /// <summary>Copies Lobster to %LOCALAPPDATA%\Programs\Lobster so shortcuts keep working.</summary>
    private bool OfferInstall(bool update)
    {
        int choice = Dialog.Show(update ? Loc.L("Mettre à jour Lobster", "Update Lobster") : Loc.L("Installer Lobster", "Install Lobster"),
            update
                ? Loc.L($"Remplacer la version installée dans {InstallDir} par celle-ci ?", $"Replace the version installed in {InstallDir} with this one?")
                : Loc.L("Lobster va se copier dans votre profil utilisateur (aucun droit administrateur requis), " +
                        "se lancer avec Windows et s'ajouter au clic droit de l'Explorateur.\n\n" + $"Emplacement : {InstallDir}",
                        "Lobster will copy itself into your user profile (no admin rights needed), " +
                        "start with Windows and add itself to the Explorer right-click menu.\n\n" + $"Location: {InstallDir}"),
            new[] { update ? Loc.L("Mettre à jour", "Update") : Loc.L("Installer", "Install"), Loc.L("Lancer sans installer", "Run without installing") }, "\uE896");
        if (choice != 0) return false;

        try
        {
            CopyToInstallDir();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
            _ownsMutex = false;
            Process.Start(new ProcessStartInfo(Path.Combine(InstallDir, "Lobster.exe")) { UseShellExecute = false, WorkingDirectory = InstallDir });
            Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("install: " + ex);
            Dialog.Error(Loc.L("Installation", "Installation"),
                Loc.L("Copie impossible : ", "Could not copy: ") + ex.Message +
                Loc.L("\nLobster va se lancer depuis son emplacement actuel.", "\nLobster will run from its current location."));
            return false;
        }
    }

    /// <summary>Single-file build: just the exe. Multi-file build (Lobster.dll beside it): the exe plus
    /// its .dll/.json files — never anything else that happens to share the folder.</summary>
    private static void CopyToInstallDir()
    {
        Directory.CreateDirectory(InstallDir);
        var srcDir = Path.GetDirectoryName(Shell.ExePath)!;
        File.Copy(Shell.ExePath, Path.Combine(InstallDir, "Lobster.exe"), overwrite: true);
        if (File.Exists(Path.Combine(srcDir, "Lobster.dll")))
        {
            foreach (var f in Directory.GetFiles(srcDir))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dll" or ".json")
                    File.Copy(f, Path.Combine(InstallDir, Path.GetFileName(f)), overwrite: true);
            }
        }
    }

    private void FirstRun()
    {
        try
        {
            if (Shell.IsPackaged)
            {
                // Store build: Windows handles the Start menu and uninstall; "Open with" comes from the package.
                Shell.StartsWithWindows = true;
            }
            else
            {
                Shell.ContextMenuInstalled = true;
                Shell.SendToInstalled = true;
                Shell.StartsWithWindows = true;
                Shell.EnsureStartMenuShortcut();
            }
        }
        catch (Exception ex) { Log.Error("first run: " + ex); }
        Settings.FirstRunDone = true;
        Settings.Save();
        OpenSettings(welcome: true);
    }

    // ================================================================ entry points

    private void HandleArgs(IEnumerable<string> args)
    {
        var list = args.ToList();
        if (list.Contains("--quit")) { Quit(); return; }
        var files = list.Where(a => !a.StartsWith("--")).ToList();
        if (files.Count > 0) _batcher?.Add(files);
        else if (list.Contains("--show")) OpenSettings();
    }

    public void OpenSettings(bool welcome = false)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        Track(new SettingsWindow(welcome));
    }

    /// <summary>Language switch: replace the settings window at the same place and size.</summary>
    public void ReopenSettings(SettingsWindow old)
    {
        var next = new SettingsWindow { Left = old.Left, Top = old.Top, Width = old.Width, Height = old.Height, WindowStartupLocation = WindowStartupLocation.Manual };
        Track(next);
        old.Close();
    }

    private void Track(SettingsWindow w)
    {
        _settingsWindow = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_settingsWindow, w)) _settingsWindow = null; };
        w.Show();
        w.Activate();
    }

    private void OpenFilesFromTray()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.L("Fichiers à traiter avec Lobster", "Files to process with Lobster"), Multiselect = true };
        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            _menu?.ShowClickMode(dlg.FileNames);
    }

    public void ShowJobs() => _jobsWindow?.Reveal();

    public void PreviewMenu() => _menu?.Preview();

    // ================================================================ tray

    private void SetupTray()
    {
        Forms.Application.EnableVisualStyles();
        System.Drawing.Icon icon;
        try
        {
            var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/lobster.ico"))!.Stream;
            icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch { icon = System.Drawing.SystemIcons.Application; }

        _tray = new Forms.NotifyIcon { Icon = icon, Text = "Lobster", Visible = true };
        BuildTrayMenu();
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowJobs(); };
        _tray.MouseDoubleClick += (_, _) => OpenSettings();
        _tray.BalloonTipClicked += (_, _) => { if (_lastOutput != null) Shell.Reveal(_lastOutput); else ShowJobs(); };
    }

    /// <summary>(Re)builds the tray menu — called again when the language changes.</summary>
    private void BuildTrayMenu()
    {
        if (_tray == null) return;
        var menu = new Forms.ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(Loc.L("Ouvrir des fichiers…", "Open files…"), null, (_, _) => OpenFilesFromTray());
        menu.Items.Add(Loc.L("Activité", "Activity"), null, (_, _) => ShowJobs());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _gestureItem = new Forms.ToolStripMenuItem(Loc.L("Geste actif", "Gesture enabled")) { CheckOnClick = true, Checked = Settings.GestureEnabled };
        _gestureItem.CheckedChanged += (_, _) =>
        {
            Settings.GestureEnabled = _gestureItem.Checked;
            Settings.Save();
        };
        menu.Items.Add(_gestureItem);
        menu.Items.Add(Loc.L("Paramètres…", "Settings…"), null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.L("Quitter Lobster", "Quit Lobster"), null, (_, _) => Quit());
        foreach (Forms.ToolStripItem item in menu.Items) item.ForeColor = System.Drawing.Color.FromArgb(240, 240, 242);
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    public void RefreshTray()
    {
        BuildTrayMenu();
        if (_jobsWindow != null && !_jobsWindow.IsVisible)
        {
            _jobsWindow.Close();
            _jobsWindow = new JobsWindow(Jobs);
        }
        try { if (!Shell.IsPackaged && Shell.ContextMenuInstalled) Shell.ContextMenuInstalled = true; } catch { }  // re-label verbs
    }

    public void Notify(string title, string text, string? revealPath = null)
    {
        if (!Settings.Notifications || _tray == null) return;
        _lastOutput = revealPath;
        _tray.ShowBalloonTip(3500, title, text, Forms.ToolTipIcon.None);
    }

    private void OnBatchFinished(IReadOnlyList<Job> batch)
    {
        int ok = batch.Count(j => j.State == JobState.Done);
        int failed = batch.Count(j => j.State == JobState.Failed);
        if (ok + failed == 0) return;
        var output = batch.Where(j => j.State == JobState.Done).SelectMany(j => j.Outputs).FirstOrDefault();
        string title = failed == 0
            ? (ok == 1 ? Loc.L("C'est prêt", "Done") : Loc.L($"{ok} fichiers prêts", $"{ok} files ready"))
            : Loc.L($"{failed} échec{(failed > 1 ? "s" : "")}", $"{failed} failed");
        string text = failed == 0
            ? (output != null ? Path.GetFileName(output.TrimEnd('\\')) + Loc.L(" — cliquez pour l'afficher", " — click to show it") : Loc.L("Terminé", "Done"))
            : (ok > 0 ? Loc.L($"{ok} réussi(s). ", $"{ok} succeeded. ") : "") + Loc.L("Détails dans la fenêtre d'activité.", "Details in the activity window.");
        if (Settings.Notifications && _tray != null)
        {
            _lastOutput = failed == 0 ? output : null;
            _tray.ShowBalloonTip(3500, title, text, failed == 0 ? Forms.ToolTipIcon.None : Forms.ToolTipIcon.Warning);
        }
        if (failed > 0) ShowJobs();
    }

    public void Quit()
    {
        Settings.Save();
        try { _hook?.Dispose(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        foreach (var j in Jobs?.All.Where(j => j.IsActive).ToList() ?? new List<Job>()) j.Cts.Cancel();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _hook?.Dispose(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        if (_ownsMutex)
        {
            // Only the real instance cleans up; a forwarding launch must not touch running jobs.
            try { _mutex?.ReleaseMutex(); } catch { }
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "Lobster"), true); } catch { }
        }
        base.OnExit(e);
    }

    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = System.Drawing.Color.FromArgb(240, 240, 242);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(232, 69, 44));
            e.Graphics.FillEllipse(b, r.X + r.Width / 2 - 4, r.Y + r.Height / 2 - 4, 8, 8);
        }
    }

    private sealed class DarkColors : Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(31, 31, 35);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(58, 35, 32);
        private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(60, 60, 66);
        public override System.Drawing.Color ToolStripDropDownBackground => Bg;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Bg;
        public override System.Drawing.Color ImageMarginGradientBegin => Bg;
        public override System.Drawing.Color ImageMarginGradientMiddle => Bg;
        public override System.Drawing.Color ImageMarginGradientEnd => Bg;
        public override System.Drawing.Color CheckBackground => Bg;
        public override System.Drawing.Color CheckSelectedBackground => Hover;
        public override System.Drawing.Color CheckPressedBackground => Hover;
    }
}
