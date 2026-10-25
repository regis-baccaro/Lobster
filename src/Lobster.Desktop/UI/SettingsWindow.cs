using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Lobster.Loc;

namespace Lobster;

public sealed class SettingsWindow : Window
{
    private readonly WrapPanel _palettes = new();
    private readonly StackPanel _tools = new();
    private readonly bool _welcome;

    public SettingsWindow(bool welcome = false)
    {
        _welcome = welcome;
        Kit.Styled(this);
        Title = L("Lobster — Paramètres", "Lobster — Settings");
        Width = 640; Height = 800; MinWidth = 520; MinHeight = 480;
        Build();
    }

    private Settings S => AppState.Settings;

    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(26, 20, 26, 24) };

        // Header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var logo = new Image { Source = Kit.Logo(), Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Kit.T("Lobster", 22, bold: true));
        titles.Children.Add(Kit.Dim(OperatingSystem.IsMacOS()
            ? L("Convertir et retoucher vos fichiers sans quitter le Finder.", "Convert and edit your files without leaving Finder.")
            : L("Convertir et retoucher vos fichiers sans quitter votre gestionnaire de fichiers.", "Convert and edit your files without leaving your file manager.")));
        header.Children.Add(titles);
        body.Children.Add(header);

        if (_welcome)
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#26E8452C")), BorderBrush = new SolidColorBrush(Color.Parse("#66E8452C")),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12), Margin = new Thickness(0, 14, 0, 0),
                Child = Kit.T(WelcomeText(), wrap: true)
            });

        // Language
        body.Children.Add(Kit.Heading(L("Langue", "Language")));
        body.Children.Add(Row(
            Radio("lang", "Français", S.Language == Lobster.Language.Fr, () => SetLanguage(Lobster.Language.Fr)),
            Radio("lang", "English", S.Language == Lobster.Language.En, () => SetLanguage(Lobster.Language.En))));

        // Palette
        body.Children.Add(Kit.Heading(L("Couleurs du menu radial", "Radial menu colors")));
        body.Children.Add(_palettes);
        var swatch = new Ellipse { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.Parse("#55FFFFFF")), StrokeThickness = 1, Margin = new Thickness(0, 0, 10, 0) };
        var hex = new TextBox { Width = 110, Text = S.CustomAccent ?? "#E8452C", FontFamily = new FontFamily("monospace") };
        void UpdateSwatch() => swatch.Fill = Palettes.TryParse(hex.Text, out var c) ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;
        UpdateSwatch();
        hex.TextChanged += (_, _) =>
        {
            UpdateSwatch();
            if (!IsLoaded || !Palettes.TryParse(hex.Text, out var c) || c.ToHex() == S.CustomAccent && S.Palette == "custom") return;
            S.CustomAccent = c.ToHex();
            S.Palette = "custom";
            S.Save();
            BuildPalettes();
            App.Current.RefreshBubble();
        };
        var customRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var preview = Kit.Btn(L("Aperçu", "Preview"), () => App.Current.PreviewMenu());
        DockPanel.SetDock(preview, Dock.Right);
        customRow.Children.Add(preview);
        var customLabel = Kit.Dim(L("Couleur personnalisée", "Custom color"), 13);
        customLabel.VerticalAlignment = VerticalAlignment.Center;
        customLabel.Margin = new Thickness(0, 0, 10, 0);
        customRow.Children.Add(Row(customLabel, swatch, hex));
        body.Children.Add(customRow);
        BuildPalettes();

        // Gesture
        body.Children.Add(Kit.Heading(L("Geste", "Gesture")));
        var gesture = App.Current.GestureName;
        if (gesture != null)
        {
            body.Children.Add(Check(L("Ouvrir le menu quand je glisse des fichiers en maintenant une touche", "Open the menu when I drag files while holding a key"),
                S.GestureEnabled, v => { S.GestureEnabled = v; S.Save(); }));
            body.Children.Add(Kit.Dim(L("Touche", "Key"), 13));
            body.Children.Add(Row(
                Radio("key", OperatingSystem.IsMacOS() ? L("Maj ⇧", "Shift ⇧") : L("Maj (Shift)", "Shift"), S.Modifier == ModifierKey.Shift, () => { S.Modifier = ModifierKey.Shift; S.Save(); }),
                Radio("key", OperatingSystem.IsMacOS() ? "Control ⌃" : "Ctrl", S.Modifier == ModifierKey.Ctrl, () => { S.Modifier = ModifierKey.Ctrl; S.Save(); }),
                Radio("key", OperatingSystem.IsMacOS() ? "Option ⌥" : "Alt", S.Modifier == ModifierKey.Alt, () => { S.Modifier = ModifierKey.Alt; S.Save(); })));
        }
        if (OperatingSystem.IsMacOS() && !MacGestureMonitor.HasPermission)
        {
            body.Children.Add(Warn(L("macOS doit autoriser Lobster dans Réglages Système › Confidentialité et sécurité › Surveillance de l'entrée, puis Lobster doit être relancé.",
                                     "macOS must allow Lobster in System Settings › Privacy & Security › Input Monitoring, then Lobster must be restarted."),
                Kit.Btn(L("Ouvrir les réglages", "Open settings"), () => AvaloniaHost.Launch("x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"))));
        }
        else if (gesture == null)
        {
            body.Children.Add(Kit.Dim(L("Votre session (Wayland) ne permet pas de surveiller la souris : utilisez la bulle Lobster — glissez vos fichiers dessus, le menu s'ouvre.",
                                        "Your session (Wayland) does not allow watching the mouse: use the Lobster bubble — drag your files onto it and the menu opens.")));
        }
        body.Children.Add(Check(L("Afficher la bulle Lobster (glissez des fichiers dessus)", "Show the Lobster bubble (drag files onto it)"),
            S.ShowBubble, v => { S.ShowBubble = v; S.Save(); App.Current.RefreshBubble(); }));

        // Output
        body.Children.Add(Kit.Heading(L("Fichiers créés", "Created files")));
        var folderBox = new TextBox { IsReadOnly = true, Text = S.CustomFolder ?? "" };
        var folderRow = new DockPanel { IsVisible = S.Output == OutputMode.Custom, Margin = new Thickness(0, 4, 0, 0) };
        async void Browse()
        {
            var f = await AppState.Host.PickFolder(L("Dossier pour les fichiers créés par Lobster", "Folder for files created by Lobster"));
            if (f == null) return;
            S.CustomFolder = f; S.Save(); folderBox.Text = f;
        }
        var browse = Kit.Btn(L("Parcourir…", "Browse…"), Browse);
        browse.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folderBox);
        void SetOut(OutputMode m) { S.Output = m; S.Save(); folderRow.IsVisible = m == OutputMode.Custom; if (m == OutputMode.Custom && string.IsNullOrEmpty(S.CustomFolder)) Browse(); }
        body.Children.Add(Row(
            Radio("out", L("À côté de l'original", "Next to the original"), S.Output == OutputMode.SameFolder, () => SetOut(OutputMode.SameFolder)),
            Radio("out", L("Sous-dossier « Lobster »", "“Lobster” subfolder"), S.Output == OutputMode.Subfolder, () => SetOut(OutputMode.Subfolder)),
            Radio("out", L("Dossier choisi", "Chosen folder"), S.Output == OutputMode.Custom, () => SetOut(OutputMode.Custom))));
        body.Children.Add(folderRow);
        body.Children.Add(Kit.Dim(L("Lobster n'écrase jamais rien : les résultats reçoivent un nouveau nom.", "Lobster never overwrites anything: results get a new name.")));

        // Integration
        if (OperatingSystem.IsMacOS())
        {
            body.Children.Add(Kit.Heading(L("Intégration macOS", "macOS integration")));
            body.Children.Add(Check(L("Action rapide « Lobster » dans le Finder (clic droit › Actions rapides)", "“Lobster” Quick Action in Finder (right-click › Quick Actions)"),
                Integration.MacIntegrated, v => Try(() => { if (v) Integration.InstallMac(); else Integration.RemoveMac(); })));
            body.Children.Add(Check(L("Ouvrir Lobster à la connexion", "Open Lobster at login"),
                Integration.StartAtLogin, v => Try(() => Integration.StartAtLogin = v)));
        }
        else
        {
            body.Children.Add(Kit.Heading(L("Intégration au bureau", "Desktop integration")));
            body.Children.Add(Check(L("Clic droit › Lobster dans Nautilus, Nemo, Caja et Dolphin, et « Ouvrir avec »", "Right-click › Lobster in Nautilus, Nemo, Caja and Dolphin, and “Open with”"),
                Integration.LinuxIntegrated, v => Try(() => { if (v) Integration.InstallLinux(); else Integration.RemoveLinux(); })));
            body.Children.Add(Check(L("Lancer Lobster à l'ouverture de session", "Start Lobster at login"),
                Integration.StartAtLogin, v => Try(() => Integration.StartAtLogin = v)));
        }
        body.Children.Add(Check(L("Notification quand un lot est terminé", "Notify me when a batch is done"),
            S.Notifications, v => { S.Notifications = v; S.Save(); }));

        // Engines
        body.Children.Add(Kit.Heading(L("Moteurs (tout reste sur votre ordinateur)", "Engines (everything stays on your computer)")));
        body.Children.Add(new Border { Background = Kit.Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Child = _tools });
        RefreshTools();

        var log = Kit.Btn(L("Journal", "Log"), () => AvaloniaHost.Launch(Log.FilePath), cls: "ghost");
        log.Margin = new Thickness(-8, 18, 0, 0);
        body.Children.Add(log);

        // Footer
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var footer = new DockPanel { Background = Kit.Surface };
        var close = Kit.Btn(L("Fermer", "Close"), Close, accent: true);
        close.Margin = new Thickness(0, 12, 22, 12);
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        var ver = Kit.Dim($"Lobster {v?.Major}.{v?.Minor}.{v?.Build} · Freia tech");
        ver.Margin = new Thickness(22, 0, 0, 0);
        ver.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(ver);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    private static string WelcomeText() => OperatingSystem.IsMacOS()
        ? L("Lobster vit dans la barre des menus. Dans le Finder, commencez à glisser des fichiers puis maintenez Maj : le menu circulaire apparaît sous le curseur. Relâchez sur une action. Vous pouvez aussi glisser vos fichiers sur la bulle Lobster.",
            "Lobster lives in the menu bar. In Finder, start dragging files, then hold Shift: the circular menu appears under the cursor. Release on an action. You can also drag your files onto the Lobster bubble.")
        : L("Glissez vos fichiers sur la bulle Lobster : le menu circulaire s'ouvre, relâchez sur une action. Sous X11, Maj + glisser fonctionne aussi partout. Clic droit › Lobster est disponible dans votre gestionnaire de fichiers.",
            "Drag your files onto the Lobster bubble: the circular menu opens, release on an action. On X11, Shift + drag also works everywhere. Right-click › Lobster is available in your file manager.");

    private void SetLanguage(Lobster.Language lang)
    {
        if (S.Language == lang) return;
        S.Language = lang;
        S.Save();
        App.Current.RefreshTray();
        if (OperatingSystem.IsLinux() && Integration.LinuxIntegrated) Try(Integration.InstallLinux);
        var next = new SettingsWindow { Width = Width, Height = Height, Position = Position, WindowStartupLocation = WindowStartupLocation.Manual };
        next.Show();
        Close();
    }

    private void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { _ = Dialogs.Error(L("Paramètres", "Settings"), ex.Message); }
    }

    // ---------------------------------------------------------------- palettes

    private void BuildPalettes()
    {
        _palettes.Children.Clear();
        foreach (var p in Palettes.All) _palettes.Children.Add(Swatch(p, S.Palette == p.Id));
        if (Palettes.TryParse(S.CustomAccent, out var custom)) _palettes.Children.Add(Swatch(Palettes.Custom(custom), S.Palette == "custom"));
    }

    private static IBrush B(Rgba c) => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));

    private Control Swatch(RadialPalette p, bool selected)
    {
        var ring = new Canvas { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Width = 44, Height = 44, Fill = B(p.Outer), Stroke = B(p.Line), StrokeThickness = 1 });
        ring.Children.Add(new Path { Fill = B(p.Accent), Data = Geometry.Parse("M22,0 A22,22 0 0 1 44,22 L32,22 A10,10 0 0 0 22,12 Z") });
        var hub = new Ellipse { Width = 22, Height = 22, Fill = B(p.Hub), Stroke = B(p.Accent), StrokeThickness = 1.5 };
        Canvas.SetLeft(hub, 11); Canvas.SetTop(hub, 11);
        ring.Children.Add(hub);
        var stack = new StackPanel();
        stack.Children.Add(ring);
        var name = Kit.T(p.Name, 11.5, brush: selected ? Kit.Text : Kit.TextDim);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(name);
        var card = new Border
        {
            Child = stack, Width = 78, Padding = new Thickness(6, 10, 6, 8), Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(10), Cursor = new Cursor(StandardCursorType.Hand),
            Background = selected ? Kit.Surface3 : Kit.Surface,
            BorderBrush = selected ? Kit.Accent : Brushes.Transparent, BorderThickness = new Thickness(1.5)
        };
        ToolTip.SetTip(card, p.Name);
        card.PointerReleased += (_, _) =>
        {
            S.Palette = p.Id;
            S.Save();
            BuildPalettes();
            App.Current.RefreshBubble();
        };
        return card;
    }

    // ---------------------------------------------------------------- engines

    private void RefreshTools()
    {
        _tools.Children.Clear();
        Tools.Reset();
        string Install(string deb, string rpm, string arch, string brew) =>
            OperatingSystem.IsMacOS() ? $"brew install {brew}" : Tools.LinuxInstallCommand(deb, rpm, arch);

        AddTool(L("Images", "Images"), L("ImageMagick intégré", "Built-in ImageMagick"), true, "PNG, JPG, WebP, AVIF, HEIC, RAW, PSD, SVG…", null);

        var ff = Tools.Ffmpeg;
        AddTool(L("Audio & vidéo", "Audio & video"), "ffmpeg", ff != null, ff ?? Install("ffmpeg", "ffmpeg", "ffmpeg", "ffmpeg"),
            ff == null ? Install("ffmpeg", "ffmpeg", "ffmpeg", "ffmpeg") : null);

        var pdf = Tools.Pdftoppm != null ? "Poppler" : Tools.Ghostscript != null ? "Ghostscript" : null;
        AddTool("PDF", "PDFsharp" + (pdf != null ? " + " + pdf : ""), pdf != null,
            pdf != null ? L("Fusion, pages, rotation, PDF → images.", "Merge, pages, rotation, PDF → images.")
                        : L("Pour PDF → images : ", "For PDF → images: ") + Install("poppler-utils", "poppler-utils", "poppler", "poppler"),
            pdf == null ? Install("poppler-utils", "poppler-utils", "poppler", "poppler") : null);

        var lo = Tools.LibreOffice;
        AddTool("Documents", lo != null ? "LibreOffice" : L("Aucun", "None"), lo != null, "Word, Excel, PowerPoint, ODF, Markdown",
            lo == null ? Install("libreoffice", "libreoffice", "libreoffice-fresh", "--cask libreoffice") : null);
    }

    private void AddTool(string title, string engine, bool ok, string detail, string? installCmd)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6) };
        var icon = Icons.View(Icons.Get(ok ? "" : ""), ok ? Kit.Ok : Kit.Accent, 15);
        icon.Margin = new Thickness(0, 2, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (installCmd != null)
        {
            var copy = Kit.Btn(L("Copier la commande", "Copy command"), () => AppState.Host.SetClipboard(installCmd), cls: "chip");
            copy.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
        }
        var text = new StackPanel();
        text.Children.Add(Kit.T($"{title} — {engine}", bold: true));
        text.Children.Add(Kit.T(detail, 12, brush: Kit.TextDim));
        row.Children.Add(text);
        _tools.Children.Add(row);
    }

    // ---------------------------------------------------------------- small builders

    private static WrapPanel Row(params Control[] items)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    private static RadioButton Radio(string group, string text, bool isChecked, Action onChecked)
    {
        var r = new RadioButton { GroupName = group, Content = text, IsChecked = isChecked };
        r.IsCheckedChanged += (_, _) => { if (r.IsChecked == true) onChecked(); };
        return r;
    }

    private static CheckBox Check(string text, bool isChecked, Action<bool> changed)
    {
        var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = isChecked };
        c.IsCheckedChanged += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    private static Control Warn(string text, Button action)
    {
        var d = new DockPanel();
        action.Margin = new Thickness(12, 0, 0, 0);
        action.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(action, Dock.Right);
        d.Children.Add(action);
        d.Children.Add(Kit.T(text, 12.5, brush: Kit.Text, wrap: true));
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#26E8452C")), BorderBrush = new SolidColorBrush(Color.Parse("#66E8452C")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Margin = new Thickness(0, 8, 0, 4), Child = d
        };
    }
}
