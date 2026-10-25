using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static Lobster.Loc;

namespace Lobster;

public partial class SettingsWindow : Window
{
    private bool _loading;

    public SettingsWindow(bool welcome = false)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"Lobster {v?.Major}.{v?.Minor}.{v?.Build} · Freia tech";

        if (welcome)
        {
            Welcome.Visibility = Visibility.Visible;
            WelcomeText.Text = L(
                "Lobster tourne dans la zone de notification. Dans l'Explorateur, commencez à glisser un ou plusieurs " +
                "fichiers puis maintenez Maj : un menu circulaire apparaît sous le curseur. Relâchez sur une action, " +
                "c'est tout. Vous pouvez aussi faire clic droit › Lobster…",
                "Lobster lives in the notification area. In Explorer, start dragging one or more files, then hold " +
                "Shift: a circular menu appears under the cursor. Release on an action, that's it. You can also " +
                "right-click › Lobster…");
        }

        Load();
        BuildPalettes();
        Hook();
        RefreshTools();
    }

    // ================================================================ load & save

    private void Load()
    {
        _loading = true;
        var s = App.Settings;
        LangFr.IsChecked = s.Language == Lobster.Language.Fr;
        LangEn.IsChecked = s.Language == Lobster.Language.En;
        GestureOn.IsChecked = s.GestureEnabled;
        KeyShift.IsChecked = s.Modifier == ModifierKey.Shift;
        KeyCtrl.IsChecked = s.Modifier == ModifierKey.Ctrl;
        KeyAlt.IsChecked = s.Modifier == ModifierKey.Alt;
        ScopeExplorer.IsChecked = s.Scope == GestureScope.ExplorerAndDesktop;
        ScopeAll.IsChecked = s.Scope == GestureScope.Everywhere;
        OutSame.IsChecked = s.Output == OutputMode.SameFolder;
        OutSub.IsChecked = s.Output == OutputMode.Subfolder;
        OutCustom.IsChecked = s.Output == OutputMode.Custom;
        CustomFolder.Text = s.CustomFolder ?? "";
        CustomRow.Visibility = s.Output == OutputMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        Notifs.IsChecked = s.Notifications;
        CustomHex.Text = s.CustomAccent ?? "#E8452C";
        UpdateCustomSwatch();
        try
        {
            Startup.IsChecked = Shell.StartsWithWindows;
            CtxMenu.IsChecked = Shell.ContextMenuInstalled;
            SendTo.IsChecked = Shell.SendToInstalled;
        }
        catch (Exception ex) { Log.Error("settings: " + ex.Message); }
        _loading = false;
    }

    private void Hook()
    {
        void Save()
        {
            if (_loading) return;
            var s = App.Settings;
            s.GestureEnabled = GestureOn.IsChecked == true;
            s.Modifier = KeyCtrl.IsChecked == true ? ModifierKey.Ctrl : KeyAlt.IsChecked == true ? ModifierKey.Alt : ModifierKey.Shift;
            s.Scope = ScopeAll.IsChecked == true ? GestureScope.Everywhere : GestureScope.ExplorerAndDesktop;
            s.Output = OutSub.IsChecked == true ? OutputMode.Subfolder : OutCustom.IsChecked == true ? OutputMode.Custom : OutputMode.SameFolder;
            s.Notifications = Notifs.IsChecked == true;
            CustomRow.Visibility = s.Output == OutputMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            if (s.Output == OutputMode.Custom && string.IsNullOrEmpty(s.CustomFolder)) OnBrowseFolder(this, new RoutedEventArgs());
            s.Save();
            App.Current.RefreshTray();
        }

        foreach (var b in new ToggleButton[] { GestureOn, KeyShift, KeyCtrl, KeyAlt, ScopeExplorer, ScopeAll, OutSame, OutSub, OutCustom, Notifs })
        {
            b.Checked += (_, _) => Save();
            b.Unchecked += (_, _) => Save();
        }

        LangFr.Checked += (_, _) => SetLanguage(Lobster.Language.Fr);
        LangEn.Checked += (_, _) => SetLanguage(Lobster.Language.En);

        CustomHex.TextChanged += (_, _) =>
        {
            UpdateCustomSwatch();
            if (_loading || !Palettes.TryParse(CustomHex.Text, out var c)) return;
            App.Settings.CustomAccent = c.ToHex();
            App.Settings.Palette = "custom";
            App.Settings.Save();
            BuildPalettes();
        };

        Startup.Click += (_, _) => Try(() => Shell.StartsWithWindows = Startup.IsChecked == true);
        CtxMenu.Click += (_, _) => Try(() => Shell.ContextMenuInstalled = CtxMenu.IsChecked == true);
        SendTo.Click += (_, _) => Try(() => Shell.SendToInstalled = SendTo.IsChecked == true);
    }

    /// <summary>Language applies at once: menus are rebuilt on every open; this window reopens itself.</summary>
    private void SetLanguage(Lobster.Language lang)
    {
        if (_loading || App.Settings.Language == lang) return;
        App.Settings.Language = lang;
        App.Settings.Save();
        App.Current.RefreshTray();
        try { if (Shell.SendToInstalled) Shell.SendToInstalled = true; } catch { }
        Dispatcher.BeginInvoke(() => App.Current.ReopenSettings(this));
    }

    private void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { Dialog.Error(L("Paramètres", "Settings"), ex.Message); Load(); }
    }

    // ================================================================ palettes

    private void BuildPalettes()
    {
        PaletteList.Children.Clear();
        foreach (var p in Palettes.All) PaletteList.Children.Add(Swatch(p, App.Settings.Palette == p.Id));
        if (Palettes.TryParse(App.Settings.CustomAccent, out var custom))
            PaletteList.Children.Add(Swatch(Palettes.Custom(custom), App.Settings.Palette == "custom"));
    }

    /// <summary>A small ring drawn in the palette's colors, with its name.</summary>
    private FrameworkElement Swatch(RadialPalette p, bool selected)
    {
        var ring = new Grid { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Fill = p.Outer.Brush(), Stroke = p.Line.Brush(), StrokeThickness = 1 });
        ring.Children.Add(new Path
        {
            Fill = p.Accent.Brush(),
            Data = Geometry.Parse("M22,0 A22,22 0 0 1 44,22 L32,22 A10,10 0 0 0 22,12 Z")
        });
        ring.Children.Add(new Ellipse { Width = 22, Height = 22, Fill = p.Hub.Brush(), Stroke = p.Accent.Brush(), StrokeThickness = 1.5 });

        var label = new TextBlock
        {
            Text = p.Name, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource(selected ? "Text" : "TextDim")
        };
        var stack = new StackPanel();
        stack.Children.Add(ring);
        stack.Children.Add(label);

        var card = new Border
        {
            Child = stack, Width = 78, Padding = new Thickness(6, 10, 6, 8), Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Background = (Brush)FindResource(selected ? "Surface3" : "Surface"),
            BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent, BorderThickness = new Thickness(1.5),
            ToolTip = p.Name
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            App.Settings.Palette = p.Id;
            App.Settings.Save();
            BuildPalettes();
        };
        return card;
    }

    private void UpdateCustomSwatch() =>
        CustomSwatch.Background = Palettes.TryParse(CustomHex.Text, out var c) ? c.Brush() : Brushes.Transparent;

    private void OnPreview(object sender, RoutedEventArgs e) => App.Current.PreviewMenu();

    // ================================================================ engines

    private void RefreshTools()
    {
        ToolList.Children.Clear();
        Tools.Reset();

        AddTool(L("Images", "Images"), L("ImageMagick intégré", "Built-in ImageMagick"), true, "PNG, JPG, WebP, AVIF, HEIC, RAW, PSD, SVG…", null);
        AddTool("PDF", L("Moteur PDF de Windows + PDFsharp", "Windows PDF engine + PDFsharp"), true,
            Tools.Ghostscript != null
                ? L("Ghostscript détecté : compression PDF optimale.", "Ghostscript found: best PDF compression.")
                : L("Ghostscript absent : la compression forte convertit les pages en images.", "No Ghostscript: strong compression turns pages into images."),
            null);

        var ff = Tools.Ffmpeg;
        AddTool(L("Audio & vidéo", "Audio & video"), "ffmpeg", ff != null, ff ?? L("Non installé", "Not installed"), ff == null
            ? new[]
            {
                (L("Installer", "Install"), (Action)(() => Requirements.InstallFfmpeg(() => RefreshTools()))),
                (L("Choisir…", "Choose…"), async () => { if (await Requirements.BrowseFfmpeg()) RefreshTools(); })
            }
            : new[] { (L("Changer…", "Change…"), (Action)(async () => { if (await Requirements.BrowseFfmpeg()) RefreshTools(); })) });

        string office = Tools.HasMsOffice
            ? "Microsoft Office" + (Tools.LibreOffice != null ? " (+ LibreOffice)" : "")
            : Tools.LibreOffice != null ? "LibreOffice" : "";
        AddTool("Documents", office.Length > 0 ? office : L("Aucun", "None"), office.Length > 0, "Word, Excel, PowerPoint, ODF, Markdown",
            office.Length == 0 ? new[] { ("LibreOffice…", (Action)(() => AppState.Host.OpenUrl("https://www.libreoffice.org/download/download-libreoffice/"))) } : null);
    }

    private void AddTool(string title, string engine, bool ok, string detail, (string label, Action act)[]? actions)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var dot = new TextBlock
        {
            Text = ok ? "" : "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
            Foreground = (Brush)FindResource(ok ? "Ok" : "Accent"), Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top
        };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        if (actions != null)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (label, act) in actions)
            {
                var b = new Button { Content = label, Style = (Style)FindResource("ChipButton") };
                b.Click += (_, _) => act();
                bar.Children.Add(b);
            }
            DockPanel.SetDock(bar, Dock.Right);
            row.Children.Add(bar);
        }
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = $"{title} — {engine}", FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Dim"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);
        ToolList.Children.Add(row);
    }

    // ================================================================ misc

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L("Dossier pour les fichiers créés par Lobster", "Folder for files created by Lobster") };
        if (dlg.ShowDialog(this) == true)
        {
            App.Settings.CustomFolder = dlg.FolderName;
            App.Settings.Save();
            CustomFolder.Text = dlg.FolderName;
        }
        else if (string.IsNullOrEmpty(App.Settings.CustomFolder))
        {
            OutSame.IsChecked = true;
        }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true }); }
        catch { Dialog.Info(L("Journal", "Log"), L("Le journal est vide pour l'instant.", "The log is empty for now.")); }
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        var r = Dialog.Show(L("Retirer Lobster de Windows", "Remove Lobster from Windows"),
            L("Supprimer le démarrage automatique, le clic droit, « Envoyer vers » et le raccourci du menu Démarrer, puis quitter Lobster ?\n\nVous pourrez ensuite supprimer Lobster.exe.",
              "Remove auto-start, the right-click entry, “Send to” and the Start menu shortcut, then quit Lobster?\n\nYou can then delete Lobster.exe."),
            new[] { L("Retirer et quitter", "Remove and quit"), L("Annuler", "Cancel") }, "");
        if (r != 0) return;
        Shell.RemoveAll();
        App.Current.Quit();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
