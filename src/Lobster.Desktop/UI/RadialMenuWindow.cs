using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using static Lobster.Loc;

namespace Lobster;

/// <summary>
/// The circular menu (Linux + macOS). Same model as on Windows:
///  • Drag mode — appears under the cursor while files are dragged (Shift-drag gesture or the bubble);
///    the user releases the files on an action.
///  • Click mode — opened from a file manager action, "Open with", the tray or a pinned drop.
/// Hit-testing is geometric (angle + radius), shared by drag and click.
/// </summary>
public sealed class RadialMenuWindow : Window
{
    public const double W = 620, H = 690;
    public const double Cx = 310, Cy = 310;
    public const double RHub = 52, R1In = 58, R1Out = 168, R2In = 174, R2Out = 286, RCatch = 296;
    private const double MaxChildDeg = 30, GapPx = 2.5;

    private enum Mode { Hidden, Armed, Drag, Busy, Click }
    public enum HitKind { None, Hub, Cat, Child }
    public readonly record struct Hit(HitKind Kind, int Cat = -1, int Child = -1);

    private Mode _mode = Mode.Hidden;
    private bool _preview;
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private List<MenuNode> _cats = new();
    private int _expanded = -1;
    private Hit _hover;
    private readonly RadialView _view;
    private readonly DispatcherTimer _releaseTimer;
    private DateTime _shownAt;

    public RadialMenuWindow()
    {
        Width = W; Height = H;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Title = "Lobster";
        WindowStartupLocation = WindowStartupLocation.Manual;

        _view = new RadialView(this);
        Content = _view;
        DragDrop.SetAllowDrop(_view, true);

        _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            if (_mode is Mode.Armed or Mode.Drag) HideMenu();
        };

        _view.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        _view.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _view.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        _view.AddHandler(DragDrop.DropEvent, OnDrop);

        _view.PointerMoved += (_, e) => { if (_mode == Mode.Click) UpdateHover(e.GetPosition(_view)); };
        _view.PointerExited += (_, _) => { if (_mode == Mode.Click) { _hover = default; _view.InvalidateVisual(); } };
        _view.PointerReleased += (_, e) =>
        {
            if (_mode != Mode.Click) return;
            if (e.InitialPressMouseButton == MouseButton.Right) { HideMenu(); return; }
            Execute(HitTest(e.GetPosition(_view)), fromDrop: false);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideMenu(); };
        Deactivated += (_, _) =>
        {
            // Ignore the deactivation that some window managers send while the menu is appearing.
            if (_mode == Mode.Click && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 400) HideMenu();
        };
    }

    // ================================================================ state exposed to the view

    internal IReadOnlyList<MenuNode> Cats => _cats;
    internal IReadOnlyList<string> Files => _files;
    internal int Expanded => _expanded;
    internal Hit Hover => _hover;
    internal bool IsDragMode => _mode == Mode.Drag;
    internal bool IsClickMode => _mode == Mode.Click;
    internal bool Revealed { get; private set; }

    // ================================================================ entry points

    /// <summary>Gesture or bubble: put an (invisible) drop target under the given screen point.</summary>
    public void ArmForDrag(PixelPoint at)
    {
        if (_mode is Mode.Click or Mode.Busy) return;
        _releaseTimer.Stop();
        _preview = false;
        _mode = Mode.Armed;
        _files = Array.Empty<string>();
        _cats = new();
        _expanded = -1;
        _hover = default;
        Revealed = false;
        _view.LoadPalette();
        ShowAt(at, activate: false);
    }

    public void OnDragReleased()
    {
        if (_mode is Mode.Armed or Mode.Drag) _releaseTimer.Start();
    }

    public void ShowClickMode(IReadOnlyList<string> files, PixelPoint? at = null, bool preview = false)
    {
        if (files.Count == 0) return;
        _releaseTimer.Stop();
        _preview = preview;
        _files = files;
        _cats = Catalog.Build(files);
        _expanded = -1;
        _hover = default;
        _mode = Mode.Click;
        _view.LoadPalette();
        ShowAt(at ?? CursorOrCenter(), activate: true);
        Reveal();
    }

    public void Preview() => ShowClickMode(new[] { System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lobster-preview.jpg") }, null, preview: true);

    /// <summary>A click outside the menu (seen by the global gesture monitor) closes it.</summary>
    public void OnGlobalMouseDown(PixelPoint p)
    {
        if (_mode != Mode.Click) return;
        double s = DesktopScaling;
        double dx = p.X - (Position.X + Cx * s), dy = p.Y - (Position.Y + Cy * s);
        if (Math.Sqrt(dx * dx + dy * dy) > RCatch * s) HideMenu();
    }

    public void HideMenu()
    {
        _releaseTimer.Stop();
        _mode = Mode.Hidden;
        _hover = default;
        _expanded = -1;
        Revealed = false;
        Hide();
    }

    // ================================================================ drag & drop

    private static List<string>? FilesOf(DragEventArgs e)
    {
        try
        {
            var items = e.Data.GetFiles();
            if (items == null) return null;
            var list = items.Select(i => i.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = FilesOf(e);
        if (files == null || _mode is Mode.Hidden or Mode.Busy or Mode.Click)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        if (_mode == Mode.Armed || !_files.SequenceEqual(files))
        {
            _files = files;
            _cats = Catalog.Build(files);
            _expanded = -1;
            _mode = Mode.Drag;
            Reveal();
        }
        UpdateHover(e.GetPosition(_view));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_mode == Mode.Armed) { OnDragEnter(sender, e); return; }
        if (_mode != Mode.Drag) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        UpdateHover(e.GetPosition(_view));
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_mode != Mode.Drag) return;
        _hover = default;
        _view.InvalidateVisual();
        // Leaving the window during a bubble drag: close unless the user comes back quickly.
        _releaseTimer.Stop();
        _releaseTimer.Start();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;   // never "Move": the source keeps its files
        if (_mode != Mode.Drag) return;
        _releaseTimer.Stop();
        var hit = HitTest(e.GetPosition(_view));
        _mode = Mode.Busy;
        Dispatcher.UIThread.Post(() => Execute(hit, fromDrop: true), DispatcherPriority.Background);
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
                else { _expanded = hit.Cat; _view.InvalidateVisual(); }
                return;
            default:
                if (fromDrop) Pin(-1);
                else HideMenu();
                return;
        }
    }

    /// <summary>Files dropped on the hub or a category: keep the menu open for clicking.</summary>
    private void Pin(int expand)
    {
        _mode = Mode.Click;
        _expanded = expand;
        _shownAt = DateTime.UtcNow;
        Activate();
        Focus();
        _view.InvalidateVisual();
    }

    private void Run(MenuNode leaf)
    {
        var files = _files.ToList();
        HideMenu();
        if (_preview) return;
        Dispatcher.UIThread.Post(() =>
        {
            try { leaf.Invoke!(files); }
            catch (Exception ex)
            {
                Log.Error($"action {leaf.Label}: {ex}");
                _ = Dialogs.Error("Lobster", ex.Message);
            }
        }, DispatcherPriority.Background);
    }

    // ================================================================ hit testing

    private static double Norm(double a) => ((a % 360) + 360) % 360;
    internal double CatWidth => 360.0 / Math.Max(1, _cats.Count);

    internal (double start, double width, int count) ChildArc(int cat)
    {
        int m = _cats[cat].Children.Count;
        double cw = Math.Min(MaxChildDeg, 360.0 / Math.Max(1, m));
        return (cat * CatWidth - cw * m / 2, cw, m);
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
        if (changed) _view.InvalidateVisual();
    }

    private void Reveal()
    {
        Revealed = true;
        _view.InvalidateVisual();
        _view.AnimateIn();
    }

    // ================================================================ placement

    private PixelPoint CursorOrCenter()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return new PixelPoint(800, 500);
        var b = screen.WorkingArea;
        return new PixelPoint(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    private void ShowAt(PixelPoint at, bool activate)
    {
        var screen = Screens.ScreenFromPoint(at) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        double s = Kit.PositionScale(screen);
        var wa = screen?.WorkingArea ?? new PixelRect(0, 0, 3840, 2160);
        double margin = (Cx - RCatch) * s;
        double left = at.X - Cx * s, top = at.Y - Cy * s;
        left = Math.Max(wa.X - margin, Math.Min(left, wa.Right - (W * s - margin)));
        top = Math.Max(wa.Y - margin, Math.Min(top, wa.Bottom - H * s));
        Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        ShowActivated = activate;
        _shownAt = DateTime.UtcNow;
        if (!IsVisible) Show();
        if (activate) { Activate(); Focus(); }
    }

    // ================================================================ drawing

    private sealed class RadialView : Control
    {
        private readonly RadialMenuWindow _w;
        private RadialPalette _p = Palettes.All[0];
        private IBrush _seg = Brushes.Black, _open = Brushes.Black, _outer = Brushes.Black, _line = Brushes.Gray,
            _accent = Brushes.Red, _onAccent = Brushes.White, _hub = Brushes.Black, _text = Brushes.White,
            _dim = Brushes.Gray, _shadow = Brushes.Black, _pill = Brushes.Black;
        private static readonly IBrush Catcher = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private readonly Typeface _semibold = new(new FontFamily("fonts:Inter#Inter, $Default"), FontStyle.Normal, FontWeight.SemiBold);
        private readonly Typeface _regular = new(new FontFamily("fonts:Inter#Inter, $Default"));
        private double _zoom = 1, _alpha = 1;
        private DispatcherTimer? _anim;

        public RadialView(RadialMenuWindow w)
        {
            _w = w;
            Width = W; Height = H;
            ClipToBounds = false;
        }

        private static IBrush B(Rgba c) => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));

        public void LoadPalette()
        {
            _p = Palettes.Current;
            _seg = B(_p.Segment); _open = B(_p.Open); _outer = B(_p.Outer); _line = B(_p.Line);
            _accent = B(_p.Accent); _onAccent = B(_p.OnAccent); _hub = B(_p.Hub);
            _text = B(_p.Text); _dim = B(_p.TextDim); _shadow = B(_p.Shadow); _pill = B(_p.Hub.WithAlpha(0xF2));
        }

        public void AnimateIn()
        {
            _anim?.Stop();
            var start = DateTime.UtcNow;
            _zoom = 0.88; _alpha = 0;
            _anim = new DispatcherTimer(TimeSpan.FromMilliseconds(12), DispatcherPriority.Render, (_, _) =>
            {
                double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / 150);
                double e = 1 - Math.Pow(1 - t, 3);
                _zoom = 0.88 + 0.12 * e; _alpha = e;
                InvalidateVisual();
                if (t >= 1) _anim?.Stop();
            });
            _anim.Start();
        }

        private static Point P(double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(Cx + r * Math.Sin(a), Cy - r * Math.Cos(a));
        }

        private static Geometry Sector(double r1, double r2, double a0, double a1)
        {
            if (a1 - a0 >= 359.9)
                return new CombinedGeometry(GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Rect(Cx - r2, Cy - r2, r2 * 2, r2 * 2)),
                    new EllipseGeometry(new Rect(Cx - r1, Cy - r1, r1 * 2, r1 * 2)));
            double go = GapPx / r2 * 180 / Math.PI, gi = GapPx / r1 * 180 / Math.PI;
            double o0 = a0 + go, o1 = a1 - go, i0 = a0 + gi, i1 = a1 - gi;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(r2, o0), true);
                c.ArcTo(P(r2, o1), new Size(r2, r2), 0, o1 - o0 > 180, SweepDirection.Clockwise);
                c.LineTo(P(r1, i1));
                c.ArcTo(P(r1, i0), new Size(r1, r1), 0, i1 - i0 > 180, SweepDirection.CounterClockwise);
                c.EndFigure(true);
            }
            return g;
        }

        private FormattedText Text(string s, double size, IBrush brush, bool bold, double maxWidth)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? _semibold : _regular, size, brush)
            {
                MaxTextWidth = maxWidth,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                MaxLineCount = 1
            };
            return ft;
        }

        private void DrawCentered(DrawingContext dc, FormattedText ft, Point center, double maxWidth) =>
            dc.DrawText(ft, new Point(center.X - maxWidth / 2, center.Y - ft.Height / 2));

        private void DrawLabel(DrawingContext dc, MenuNode n, bool hot, bool inner, double maxWidth, Point center)
        {
            var fg = hot ? _onAccent : _text;
            var title = Text(n.Label, inner ? 11.5 : 12.5, fg, true, maxWidth);
            FormattedText? sub = !inner && n.Sub != null ? Text(n.Sub, 10, hot ? _onAccent : _dim, false, maxWidth) : null;
            bool icon = inner && n.Glyph != null;
            double h = title.Height + (sub?.Height ?? 0) + (icon ? 22 : 0);
            double y = center.Y - h / 2;
            if (icon)
            {
                Icons.Draw(dc, n.Glyph, new Point(center.X, y + 9), 18, fg);
                y += 22;
            }
            dc.DrawText(title, new Point(center.X - maxWidth / 2, y));
            if (sub != null) dc.DrawText(sub, new Point(center.X - maxWidth / 2, y + title.Height));
        }

        public override void Render(DrawingContext dc)
        {
            // Always paint a near-invisible disc so the window is a drop target.
            dc.DrawEllipse(Catcher, null, new Point(Cx, Cy), RCatch, RCatch);
            if (!_w.Revealed || _w.Cats.Count == 0) return;

            using var _o = dc.PushOpacity(_alpha);
            using var _t = dc.PushTransform(Matrix.CreateTranslation(-Cx, -Cy) * Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(Cx, Cy));

            var cats = _w.Cats;
            int expanded = _w.Expanded;
            var hover = _w.Hover;
            bool outerOpen = expanded >= 0 && cats[expanded].Children.Count > 0;

            // Soft shadow.
            double sr = outerOpen ? R2Out : R1Out;
            for (int k = 0; k < 6; k++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(_p.Shadow.A / 7), 0, 0, 0)), null, new Point(Cx, Cy + 6), sr + 14 - k * 3, sr + 14 - k * 3);

            if (outerOpen)
            {
                var (start, cw, m) = _w.ChildArc(expanded);
                double midR = (R2In + R2Out) / 2;
                double lw = Math.Max(40, Math.Min(96, cw * Math.PI / 180 * midR - 8));
                for (int j = 0; j < m; j++)
                {
                    bool hot = hover.Kind == HitKind.Child && hover.Child == j;
                    double a0 = start + j * cw, a1 = a0 + cw;
                    dc.DrawGeometry(hot ? _accent : _outer, new Pen(_line, 1), Sector(R2In, R2Out, a0, a1));
                    DrawLabel(dc, cats[expanded].Children[j], hot, false, lw, P(midR, (a0 + a1) / 2));
                }
            }

            double w = _w.CatWidth, midIn = (R1In + R1Out) / 2;
            double innerLabel = Math.Max(52, Math.Min(96, w * Math.PI / 180 * midIn - 6));
            for (int i = 0; i < cats.Count; i++)
            {
                bool hot = hover.Kind == HitKind.Cat && hover.Cat == i;
                bool open = expanded == i;
                double a0 = i * w - w / 2, a1 = a0 + w;
                dc.DrawGeometry(hot ? _accent : open ? _open : _seg, new Pen(open && !hot ? _accent : _line, 1), Sector(R1In, R1Out, a0, a1));
                DrawLabel(dc, cats[i], hot, true, innerLabel, P(midIn, i * w));
            }

            // Hub.
            bool hubHot = hover.Kind == HitKind.Hub;
            dc.DrawEllipse(_hub, new Pen(_accent, hubHot ? 3 : 1.5), new Point(Cx, Cy), RHub, RHub);
            var kind = FileKinds.Common(_w.Files);
            Icons.Draw(dc, _w.IsClickMode && hubHot ? "" : FileKinds.Glyph(kind), new Point(Cx, Cy - 10), 20, _accent);
            var desc = Text(FileKinds.Describe(_w.Files), 11, _text, true, RHub * 2 - 14);
            DrawCentered(dc, desc, new Point(Cx, Cy + 14), RHub * 2 - 14);

            // Hint pill.
            string hint;
            if (hover.Kind == HitKind.Child) hint = cats[hover.Cat].Children[hover.Child].Hint ?? cats[hover.Cat].Children[hover.Child].Label;
            else if (hover.Kind == HitKind.Cat && cats[hover.Cat].IsLeaf) hint = cats[hover.Cat].Hint ?? cats[hover.Cat].Label;
            else if (hover.Kind == HitKind.Hub) hint = _w.IsDragMode ? L("Relâcher ici pour garder le menu ouvert", "Release here to keep the menu open") : L("Fermer", "Close");
            else hint = _w.IsDragMode ? L("Relâchez sur une action", "Release on an action") : L("Choisissez une action · Échap pour fermer", "Pick an action · Esc to close");
            var ht = Text(hint, 12, _text, false, 560);
            double pw = Math.Min(560, ht.WidthIncludingTrailingWhitespace) + 28, ph = ht.Height + 13;
            var pill = new Rect(Cx - pw / 2, Cy + RCatch + 10, pw, ph);
            dc.DrawRectangle(_pill, new Pen(_line, 1), pill, ph / 2, ph / 2);
            dc.DrawText(ht, new Point(Cx - 280, pill.Y + 6));
        }
    }
}
