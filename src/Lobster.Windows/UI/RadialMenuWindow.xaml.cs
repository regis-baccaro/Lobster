using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Lobster;

/// <summary>
/// The circular menu. Two ways in:
///  • Drag mode — appears under the cursor while files are dragged with the modifier key;
///    the user releases the files on an action.
///  • Click mode — opened from the Explorer context menu / tray; the user clicks an action.
/// Hit-testing is done geometrically (angle + radius), so drag and click share one code path.
/// </summary>
public partial class RadialMenuWindow : Window
{
    // Geometry (DIPs)
    private const double Cx = 310, Cy = 310;
    private const double RHub = 52, R1In = 58, R1Out = 168, R2In = 174, R2Out = 286, RCatch = 296;
    private const double MaxChildDeg = 30, GapPx = 2.5;

    private enum Mode { Hidden, Armed, Drag, Busy, Click }
    private enum HitKind { None, Hub, Cat, Child }
    private readonly record struct Hit(HitKind Kind, int Cat = -1, int Child = -1);

    private Mode _mode = Mode.Hidden;
    private bool _preview;
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private List<MenuNode> _cats = new();
    private int _expanded = -1;
    private Hit _hover;
    private IntPtr _hwnd;
    private readonly DropHelper _dropHelper = new();
    private readonly DispatcherTimer _releaseTimer;

    // Brushes
    // Brushes, refreshed from the chosen palette each time the menu opens.
    private Brush SegFill = Brushes.Black, SegOpen = Brushes.Black, OuterFill = Brushes.Black, SegLine = Brushes.Gray,
        Accent = Brushes.Red, OnAccent = Brushes.White, HubFill = Brushes.Black, TextMain = Brushes.White,
        TextDim = Brushes.Gray, ShadowFill = Brushes.Black, PillFill = Brushes.Black;

    private void LoadPalette()
    {
        var p = Palettes.Current;
        SegFill = p.Segment.Brush(); SegOpen = p.Open.Brush(); OuterFill = p.Outer.Brush(); SegLine = p.Line.Brush();
        Accent = p.Accent.Brush(); OnAccent = p.OnAccent.Brush(); HubFill = p.Hub.Brush();
        TextMain = p.Text.Brush(); TextDim = p.TextDim.Brush(); ShadowFill = p.Shadow.Brush();
        PillFill = p.Hub.WithAlpha(0xF2).Brush();
    }

    /// <summary>Settings preview: show the menu for a sample image in click mode.</summary>
    public void Preview() => ShowClickMode(new[] { System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lobster-preview.jpg") }, preview: true);
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public RadialMenuWindow()
    {
        InitializeComponent();
        _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            if (_mode is Mode.Armed or Mode.Drag) HideMenu();
        };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        MouseMove += (_, e) => { if (_mode == Mode.Click) UpdateHover(e.GetPosition(Stage)); };
        MouseLeave += (_, _) => { if (_mode == Mode.Click) { _hover = default; Render(); } };
        MouseLeftButtonUp += (_, e) => { if (_mode == Mode.Click) Execute(HitTest(e.GetPosition(Stage)), fromDrop: false); };
        MouseRightButtonUp += (_, _) => { if (_mode == Mode.Click) HideMenu(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideMenu(); };
        Deactivated += (_, _) => { if (_mode == Mode.Click) HideMenu(); };
    }

    public void Prepare()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetNoActivate(true);
    }

    // ================================================================ entry points

    /// <summary>Gesture detected: put an invisible drop target under the cursor.</summary>
    public void ArmForDrag(int x, int y)
    {
        if (_mode == Mode.Click || _mode == Mode.Busy) return;
        // A modal dialog disables this window: it could not receive the drop.
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) return;
        _releaseTimer.Stop();
        _preview = false;
        LoadPalette();
        _mode = Mode.Armed;
        _files = Array.Empty<string>();
        _cats = new();
        _expanded = -1;
        _hover = default;
        Layer.Opacity = 0;
        Layer.Children.Clear();
        SetNoActivate(true);
        ShowAt(x, y);
    }

    /// <summary>Global mouse-down (from the hook): a click outside closes a pinned menu.</summary>
    public void OnGlobalMouseDown(int x, int y)
    {
        if (_mode != Mode.Click) return;
        Win32.GetWindowRect(_hwnd, out var r);
        double s = Math.Max(0.5, (r.Right - r.Left) / Width);
        double dx = x - (r.Left + Cx * s), dy = y - (r.Top + Cy * s);
        if (Math.Sqrt(dx * dx + dy * dy) > RCatch * s) HideMenu();
    }

    public void OnDragReleased()
    {
        if (_mode is Mode.Armed or Mode.Drag) _releaseTimer.Start();
    }

    /// <summary>Open the menu for given files, waiting for a click.</summary>
    public void ShowClickMode(IReadOnlyList<string> files, bool preview = false)
    {
        if (files.Count == 0) return;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
        {
            // A Lobster dialog is open (crop, trim…): show the menu once it closes.
            var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            wait.Tick += (_, _) =>
            {
                if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) return;
                wait.Stop();
                ShowClickMode(files, preview);
            };
            wait.Start();
            return;
        }
        _preview = preview;
        LoadPalette();
        _releaseTimer.Stop();
        _files = files;
        _cats = Catalog.Build(files);
        _expanded = -1;
        _hover = default;
        _mode = Mode.Click;
        SetNoActivate(false);
        var p = Win32.Cursor();
        ShowAt(p.X, p.Y);
        Activate();
        Win32.ForceForeground(_hwnd);
        Focus();
        Reveal();
    }

    public void HideMenu()
    {
        _releaseTimer.Stop();
        _mode = Mode.Hidden;
        _hover = default;
        _expanded = -1;
        Layer.Opacity = 0;
        Layer.Children.Clear();
        Hide();
    }

    // ================================================================ drag & drop

    private static string[]? FilesOf(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            return data.GetData(DataFormats.FileDrop) as string[];
        }
        catch { return null; }
    }

    private static DragDropEffects CopyEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy :
        (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.None;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = FilesOf(e.Data);
        if (files == null || files.Length == 0 || _mode is Mode.Hidden or Mode.Busy or Mode.Click)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = CopyEffect(e.AllowedEffects);
        _dropHelper.Enter(_hwnd, e.Data, (int)e.Effects);

        if (_mode == Mode.Armed || !_files.SequenceEqual(files))
        {
            _files = files;
            _cats = Catalog.Build(files);
            _expanded = -1;
            _mode = Mode.Drag;
            Reveal();
        }
        UpdateHover(e.GetPosition(Stage));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_mode != Mode.Drag) { e.Effects = DragDropEffects.None; return; }
        e.Effects = CopyEffect(e.AllowedEffects);
        _dropHelper.Over((int)e.Effects);
        UpdateHover(e.GetPosition(Stage));
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dropHelper.Leave();
        if (_mode != Mode.Drag) return;
        _hover = default;
        Render();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = CopyEffect(e.AllowedEffects); // never "Move": the source keeps its files
        _dropHelper.Drop(e.Data, (int)e.Effects);
        if (_mode != Mode.Drag) return;
        var hit = HitTest(e.GetPosition(Stage));
        _mode = Mode.Busy;
        // Return to Explorer immediately; act once the OLE call has unwound.
        Dispatcher.BeginInvoke(() => Execute(hit, fromDrop: true), DispatcherPriority.Background);
    }

    // ================================================================ actions

    private void Execute(Hit hit, bool fromDrop)
    {
        switch (hit.Kind)
        {
            case HitKind.Child:
                Run(_cats[hit.Cat].Children[hit.Child]);
                return;
            case HitKind.Cat when _cats[hit.Cat].IsLeaf:
                Run(_cats[hit.Cat]);
                return;
            case HitKind.Cat:
                if (fromDrop) Pin(hit.Cat);
                else { _expanded = hit.Cat; Render(); }
                return;
            default:
                if (fromDrop) Pin(-1);
                else HideMenu();
                return;
        }
    }

    /// <summary>Files were dropped on the hub or a category: keep the menu open for clicking.</summary>
    private void Pin(int expand)
    {
        _mode = Mode.Click;
        _expanded = expand;
        SetNoActivate(false);
        Activate();
        Win32.ForceForeground(_hwnd);
        Focus();
        Render();
    }

    private void Run(MenuNode leaf)
    {
        var files = _files.ToList();
        HideMenu();
        if (_preview) return;   // settings preview: nothing to run
        Dispatcher.BeginInvoke(() =>
        {
            try { leaf.Invoke!(files); }
            catch (Exception ex)
            {
                Log.Error($"action {leaf.Label}: {ex}");
                Dialog.Error("Lobster", ex.Message);
            }
        }, DispatcherPriority.Background);
    }

    // ================================================================ hit testing

    private static double Norm(double a) => ((a % 360) + 360) % 360;
    private double CatWidth => 360.0 / Math.Max(1, _cats.Count);

    private (double start, double width, int count) ChildArc(int cat)
    {
        int m = _cats[cat].Children.Count;
        double cw = Math.Min(MaxChildDeg, 360.0 / Math.Max(1, m));
        double total = cw * m;
        return (cat * CatWidth - total / 2, cw, m);
    }

    private Hit HitTest(Point p)
    {
        double dx = p.X - Cx, dy = p.Y - Cy;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double angle = Norm(Math.Atan2(dx, -dy) * 180 / Math.PI);
        if (_cats.Count == 0) return default;
        if (r <= RHub + 3) return new Hit(HitKind.Hub);
        if (r <= R1Out + 2)
        {
            int idx = (int)(Norm(angle + CatWidth / 2) / CatWidth) % _cats.Count;
            return new Hit(HitKind.Cat, idx);
        }
        if (r <= RCatch && _expanded >= 0 && _cats[_expanded].Children.Count > 0)
        {
            var (start, cw, m) = ChildArc(_expanded);
            double d = Norm(angle - start);
            if (d < cw * m) return new Hit(HitKind.Child, _expanded, Math.Min(m - 1, (int)(d / cw)));
        }
        return default;
    }

    private void UpdateHover(Point p)
    {
        var hit = HitTest(p);
        bool changed = hit != _hover;
        _hover = hit;
        if (hit.Kind == HitKind.Cat && _expanded != hit.Cat)
        {
            _expanded = _cats[hit.Cat].IsLeaf ? -1 : hit.Cat;
            changed = true;
        }
        if (changed) Render();
    }

    // ================================================================ rendering

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Point P(double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(Cx + r * Math.Sin(a), Cy - r * Math.Cos(a));
    }

    private static Geometry Sector(double r1, double r2, double a0, double a1)
    {
        if (a1 - a0 >= 359.9)
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(Cx, Cy), r2, r2),
                new EllipseGeometry(new Point(Cx, Cy), r1, r1));
            ring.Freeze();
            return ring;
        }
        double go = GapPx / r2 * 180 / Math.PI, gi = GapPx / r1 * 180 / Math.PI;
        double o0 = a0 + go, o1 = a1 - go, i0 = a0 + gi, i1 = a1 - gi;
        var fig = new PathFigure { StartPoint = P(r2, o0), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment(P(r2, o1), new Size(r2, r2), 0, o1 - o0 > 180, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(r1, i1), true));
        fig.Segments.Add(new ArcSegment(P(r1, i0), new Size(r1, r1), 0, i1 - i0 > 180, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    private void AddSector(double r1, double r2, double a0, double a1, Brush fill, Brush? stroke = null)
    {
        Layer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Sector(r1, r2, a0, a1),
            Fill = fill,
            Stroke = stroke ?? SegLine,
            StrokeThickness = 1
        });
    }

    private void AddCentered(UIElement el, Point at)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(el, Math.Round(at.X - el.DesiredSize.Width / 2));
        Canvas.SetTop(el, Math.Round(at.Y - el.DesiredSize.Height / 2));
        Layer.Children.Add(el);
    }

    private StackPanel Label(MenuNode n, bool hot, bool inner, double maxWidth)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (inner && n.Glyph != null)
            sp.Children.Add(new TextBlock
            {
                Text = n.Glyph, FontFamily = IconFont, FontSize = 18,
                Foreground = hot ? OnAccent : TextMain,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4)
            });
        sp.Children.Add(new TextBlock
        {
            Text = n.Label, FontSize = inner ? 11.5 : 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = hot ? OnAccent : TextMain, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        if (!inner && n.Sub != null)
            sp.Children.Add(new TextBlock
            {
                Text = n.Sub, FontSize = 10, Foreground = hot ? OnAccent : TextDim,
                TextAlignment = TextAlignment.Center, MaxWidth = maxWidth,
                TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center
            });
        return sp;
    }

    private void Render()
    {
        Layer.Children.Clear();
        if (_cats.Count == 0) return;

        // Soft shadow under the whole menu.
        var shadowDisc = _expanded >= 0 && !_cats[_expanded].IsLeaf ? R2Out : R1Out;
        var disc = new System.Windows.Shapes.Ellipse
        {
            Width = shadowDisc * 2, Height = shadowDisc * 2, Fill = ShadowFill,
            Effect = new BlurEffect { Radius = 26 }
        };
        Canvas.SetLeft(disc, Cx - shadowDisc); Canvas.SetTop(disc, Cy - shadowDisc + 6);
        Layer.Children.Add(disc);

        // Outer ring (children of the open category).
        if (_expanded >= 0 && _cats[_expanded].Children.Count > 0)
        {
            var (start, cw, m) = ChildArc(_expanded);
            double midR = (R2In + R2Out) / 2;
            double labelWidth = Math.Max(40, Math.Min(96, cw * Math.PI / 180 * midR - 8));
            for (int j = 0; j < m; j++)
            {
                bool hot = _hover.Kind == HitKind.Child && _hover.Child == j;
                double a0 = start + j * cw, a1 = a0 + cw;
                AddSector(R2In, R2Out, a0, a1, hot ? Accent : OuterFill);
                AddCentered(Label(_cats[_expanded].Children[j], hot, false, labelWidth), P(midR, (a0 + a1) / 2));
            }
        }

        // Inner ring (categories).
        double w = CatWidth, midIn = (R1In + R1Out) / 2;
        double innerLabel = Math.Max(52, Math.Min(96, w * Math.PI / 180 * midIn - 6));
        for (int i = 0; i < _cats.Count; i++)
        {
            bool hot = _hover.Kind == HitKind.Cat && _hover.Cat == i;
            bool open = _expanded == i;
            double a0 = i * w - w / 2, a1 = a0 + w;
            AddSector(R1In, R1Out, a0, a1, hot ? Accent : open ? SegOpen : SegFill, open && !hot ? Accent : null);
            AddCentered(Label(_cats[i], hot, true, innerLabel), P(midIn, i * w));
        }

        // Hub.
        bool hubHot = _hover.Kind == HitKind.Hub;
        var hub = new System.Windows.Shapes.Ellipse
        {
            Width = RHub * 2, Height = RHub * 2, Fill = HubFill,
            Stroke = Accent, StrokeThickness = hubHot ? 3 : 1.5
        };
        Canvas.SetLeft(hub, Cx - RHub); Canvas.SetTop(hub, Cy - RHub);
        Layer.Children.Add(hub);
        var kind = FileKinds.Common(_files);
        var hubText = new StackPanel();
        hubText.Children.Add(new TextBlock
        {
            Text = _mode == Mode.Click && hubHot ? "" : FileKinds.Glyph(kind),
            FontFamily = IconFont, FontSize = 20, Foreground = Accent,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3)
        });
        hubText.Children.Add(new TextBlock
        {
            Text = FileKinds.Describe(_files), FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = TextMain, HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = RHub * 2 - 14, TextTrimming = TextTrimming.CharacterEllipsis
        });
        AddCentered(hubText, new Point(Cx, Cy));

        // Hint pill under the menu.
        AddHint();
    }

    private void AddHint()
    {
        string text;
        if (_hover.Kind == HitKind.Child) text = _cats[_hover.Cat].Children[_hover.Child].Hint ?? _cats[_hover.Cat].Children[_hover.Child].Label;
        else if (_hover.Kind == HitKind.Cat && _cats[_hover.Cat].IsLeaf) text = _cats[_hover.Cat].Hint ?? _cats[_hover.Cat].Label;
        else if (_hover.Kind == HitKind.Hub) text = _mode == Mode.Drag ? Loc.L("Relâcher ici pour garder le menu ouvert", "Release here to keep the menu open") : Loc.L("Fermer", "Close");
        else text = _mode == Mode.Drag ? Loc.L("Relâchez sur une action", "Release on an action") : Loc.L("Choisissez une action · Échap pour fermer", "Pick an action · Esc to close");

        var pill = new Border
        {
            Background = PillFill,
            BorderBrush = SegLine, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 6, 14, 7),
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = TextMain }
        };
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(pill, Math.Round(Cx - pill.DesiredSize.Width / 2));
        Canvas.SetTop(pill, Cy + RCatch + 10);
        Layer.Children.Add(pill);
    }

    private void Reveal()
    {
        Render();
        if (Layer.Opacity >= 1) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(150);
        Layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, dur) { EasingFunction = ease });
        Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, dur) { EasingFunction = ease });
    }

    // ================================================================ window plumbing

    private void SetNoActivate(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_TOOLWINDOW;
        ex = on ? ex | Win32.WS_EX_NOACTIVATE : ex & ~Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void ShowAt(int x, int y)
    {
        // Stop any running animation so Opacity really is 0 while we move.
        Layer.BeginAnimation(OpacityProperty, null);
        Layer.Opacity = 0;
        CenterOn(x, y);
        if (!IsVisible) Show();
        CenterOn(x, y);
        // A monitor with another DPI resizes the window: center again once that settles.
        Dispatcher.BeginInvoke(() => CenterOn(x, y), DispatcherPriority.Loaded);
    }

    private void CenterOn(int x, int y)
    {
        Win32.GetWindowRect(_hwnd, out var r);
        double scale = Math.Max(0.5, (r.Right - r.Left) / Width);
        var wa = Win32.WorkAreaAt(new Win32.POINT(x, y));
        double left = x - Cx * scale, top = y - Cy * scale;
        double margin = (Cx - RCatch) * scale;
        left = Math.Max(wa.Left - margin, Math.Min(left, wa.Right - (Width - margin / scale) * scale));
        top = Math.Max(wa.Top - margin, Math.Min(top, wa.Bottom - Height * scale));
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
}
