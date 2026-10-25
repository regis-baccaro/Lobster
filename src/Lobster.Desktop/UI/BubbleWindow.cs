using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using static Lobster.Loc;

namespace Lobster;

/// <summary>
/// A small floating Lobster bubble. Drag files onto it and the radial menu unfolds under the
/// cursor — the drag-to-convert gesture where global mouse monitoring is not allowed (Wayland).
/// Drag the bubble itself to move it; click it for the menu (open files, settings).
/// </summary>
public sealed class BubbleWindow : Window
{
    private const double Size = 64;
    private readonly Control _face;
    private bool _hot;
    private Point? _pressAt;

    public event Action<PixelPoint>? FilesApproaching;
    public event Action<System.Collections.Generic.IReadOnlyList<string>>? FilesDropped;
    public event Action? Clicked;

    public BubbleWindow()
    {
        Width = Size + 16; Height = Size + 16;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Title = "Lobster";
        WindowStartupLocation = WindowStartupLocation.Manual;
        ToolTip.SetTip(this, L("Glissez des fichiers ici", "Drag files here"));

        _face = new Face(this) { Width = Size + 16, Height = Size + 16 };
        Content = _face;
        DragDrop.SetAllowDrop(_face, true);

        _face.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (!HasFiles(e)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy;
            _hot = true;
            _face.InvalidateVisual();
            // Unfold the radial menu centred on the bubble; it becomes the drop target.
            var s = DesktopScaling;
            FilesApproaching?.Invoke(new PixelPoint(Position.X + (int)(Bounds.Width * s / 2), Position.Y + (int)(Bounds.Height * s / 2)));
        });
        _face.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None);
        _face.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _hot = false; _face.InvalidateVisual(); });
        _face.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            _hot = false;
            _face.InvalidateVisual();
            e.DragEffects = DragDropEffects.Copy;
            var files = e.Data.GetFiles()?.Select(i => i.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
            if (files is { Count: > 0 }) FilesDropped?.Invoke(files);
        });

        _face.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressAt = e.GetPosition(this);
            BeginMoveDrag(e);
        };
        _face.PointerReleased += (_, e) =>
        {
            if (_pressAt is { } p && Point.Distance(p, e.GetPosition(this)) < 4) Clicked?.Invoke();
            _pressAt = null;
        };
        PositionChanged += (_, _) =>
        {
            if (!IsVisible) return;
            AppState.Settings.BubbleX = Position.X;
            AppState.Settings.BubbleY = Position.Y;
            AppState.Settings.SaveSoon();
        };
    }

    private static bool HasFiles(DragEventArgs e)
    {
        try { return e.Data.Contains(DataFormats.Files); } catch { return false; }
    }

    public void ShowAtSavedPosition()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        var s = AppState.Settings;
        PixelPoint p;
        if (s.BubbleX is int x && s.BubbleY is int y && Screens.All.Any(sc => sc.Bounds.Contains(new PixelPoint(x + 10, y + 10))))
            p = new PixelPoint(x, y);
        else if (screen != null)
            p = new PixelPoint((int)(screen.WorkingArea.Right - 110 * Kit.PositionScale(screen)), (int)(screen.WorkingArea.Y + screen.WorkingArea.Height * 0.55));
        else p = new PixelPoint(100, 100);
        Position = p;
        Show();
    }

    private sealed class Face : Control
    {
        private readonly BubbleWindow _w;
        private Avalonia.Media.Imaging.Bitmap? _logo;
        public Face(BubbleWindow w) { _w = w; }

        public override void Render(DrawingContext dc)
        {
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var p = Palettes.Current;
            var accent = new SolidColorBrush(Color.FromArgb(p.Accent.A, p.Accent.R, p.Accent.G, p.Accent.B));
            double r = Size / 2 + (_w._hot ? 4 : 0);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), null, new Point(c.X, c.Y + 3), r + 2, r + 2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xF2, 0x1D, 0x1D, 0x21)), new Pen(accent, _w._hot ? 3 : 2), c, r, r);
            // The Lobster logo, clipped to a circle.
            _logo ??= Kit.Logo();
            if (_logo != null)
            {
                double lr = r - 7;
                using (dc.PushGeometryClip(new EllipseGeometry(new Rect(c.X - lr, c.Y - lr, lr * 2, lr * 2))))
                    dc.DrawImage(_logo, new Rect(c.X - lr - 4, c.Y - lr - 4, (lr + 4) * 2, (lr + 4) * 2));
            }
        }
    }
}
