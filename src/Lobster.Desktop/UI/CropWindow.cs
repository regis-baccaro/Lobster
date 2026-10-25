using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using static Lobster.Loc;

namespace Lobster;

/// <summary>Interactive crop: drag to draw, move or resize a frame; optional fixed ratio.</summary>
public sealed class CropWindow : Window
{
    public CropRect? Selection { get; private set; }

    private readonly double _iw, _ih;
    private readonly int _origW, _origH;
    private double _scale = 1;
    private Rect _sel;
    private double? _ratio;

    private enum DragKind { None, Move, Resize, New }
    private DragKind _drag;
    private bool _l, _t, _r, _b;
    private Point _grab;
    private Rect _start;

    private readonly Canvas _board = new() { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Cross) };
    private readonly Image _pic = new() { Stretch = Stretch.Fill };
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Grid _host = new() { Margin = new Thickness(16) };
    private readonly TextBlock _size = Kit.Dim("");
    private readonly StackPanel _ratios = new() { Orientation = Orientation.Horizontal };
    private readonly List<Button> _ratioButtons = new();

    public CropWindow(string previewPath, int originalWidth, int originalHeight, string? note)
    {
        Kit.Styled(this);
        Title = L("Rogner — Lobster", "Crop — Lobster");
        Width = 960; Height = 720; MinWidth = 620; MinHeight = 460;

        var bmp = new Bitmap(previewPath);
        _pic.Source = bmp;
        _iw = bmp.PixelSize.Width;
        _ih = bmp.PixelSize.Height;
        _origW = originalWidth > 0 ? originalWidth : (int)_iw;
        _origH = originalHeight > 0 ? originalHeight : (int)_ih;
        _sel = new Rect(_iw * 0.08, _ih * 0.08, _iw * 0.84, _ih * 0.84);

        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var label = Kit.Dim(L("Format", "Ratio"));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(label, Dock.Left);
        top.Children.Add(label);
        var noteText = Kit.Dim(note ?? "");
        noteText.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(noteText, Dock.Right);
        top.Children.Add(noteText);
        top.Children.Add(_ratios);
        foreach (var (name, ratio) in new (string, double?)[]
                 {
                     (L("Libre", "Free"), null), ("Original", _iw / _ih), ("1:1", 1), ("4:3", 4.0 / 3), ("3:2", 1.5),
                     ("16:9", 16.0 / 9), ("9:16", 9.0 / 16), ("4:5", 0.8)
                 })
        {
            Button? b = null;
            b = Kit.Btn(name, () => SetRatio(ratio, b!), cls: "chip");
            _ratioButtons.Add(b);
            _ratios.Children.Add(b);
        }
        Highlight(_ratioButtons[0]);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Kit.Btn(L("Réinitialiser", "Reset"), Reset, cls: "ghost"));
        buttons.Children.Add(Kit.Btn(L("Annuler", "Cancel"), Close));
        var apply = Kit.Btn(L("Rogner", "Crop"), Apply, accent: true);
        apply.IsDefault = true;
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        _size.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(_size);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _board.Children.Add(_pic);
        _board.Children.Add(_shade);
        _board.Children.Add(_overlay);
        _board.HorizontalAlignment = HorizontalAlignment.Center;
        _board.VerticalAlignment = VerticalAlignment.Center;
        _host.Children.Add(_board);
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#0D0D0F")), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _host });

        Content = root;

        _host.SizeChanged += (_, _) => LayoutBoard();
        _board.PointerPressed += OnDown;
        _board.PointerMoved += OnMove;
        _board.PointerReleased += (_, _) => _drag = DragKind.None;
        _board.PointerCaptureLost += (_, _) => _drag = DragKind.None;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void Highlight(Button active)
    {
        foreach (var b in _ratioButtons)
        {
            b.Background = b == active ? Kit.Accent : Kit.Surface2;
            b.Foreground = b == active ? Brushes.White : Kit.Text;
        }
    }

    private void SetRatio(double? ratio, Button source)
    {
        _ratio = ratio;
        Highlight(source);
        if (ratio is double r)
        {
            double w = _iw, h = w / r;
            if (h > _ih) { h = _ih; w = h * r; }
            w *= 0.9; h *= 0.9;
            var c = _sel.Center;
            _sel = new Rect(Math.Clamp(c.X - w / 2, 0, _iw - w), Math.Clamp(c.Y - h / 2, 0, _ih - h), w, h);
        }
        Draw();
    }

    private void LayoutBoard()
    {
        double aw = _host.Bounds.Width, ah = _host.Bounds.Height;
        if (aw <= 0 || ah <= 0) return;
        _scale = Math.Min(aw / _iw, ah / _ih);
        _board.Width = _iw * _scale; _board.Height = _ih * _scale;
        _pic.Width = _board.Width; _pic.Height = _board.Height;
        Draw();
    }

    private Rect Disp => new(_sel.X * _scale, _sel.Y * _scale, _sel.Width * _scale, _sel.Height * _scale);

    private static Point[] HandlePoints(Rect r) => new[]
    {
        r.TopLeft, new Point(r.X + r.Width / 2, r.Y), r.TopRight, new Point(r.Right, r.Y + r.Height / 2),
        r.BottomRight, new Point(r.X + r.Width / 2, r.Bottom), r.BottomLeft, new Point(r.X, r.Y + r.Height / 2)
    };

    private static readonly (bool l, bool t, bool r, bool b)[] HandleEdges =
    {
        (true, true, false, false), (false, true, false, false), (false, true, true, false), (false, false, true, false),
        (false, false, true, true), (false, false, false, true), (true, false, false, true), (true, false, false, false)
    };

    private void Draw()
    {
        var full = new Rect(0, 0, _iw * _scale, _ih * _scale);
        var r = Disp;
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(r));
        _overlay.Children.Clear();
        var faint = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        for (int i = 1; i <= 2; i++)
        {
            _overlay.Children.Add(new Line { StartPoint = new Point(r.X + r.Width * i / 3, r.Y), EndPoint = new Point(r.X + r.Width * i / 3, r.Bottom), Stroke = faint, StrokeThickness = 1 });
            _overlay.Children.Add(new Line { StartPoint = new Point(r.X, r.Y + r.Height * i / 3), EndPoint = new Point(r.Right, r.Y + r.Height * i / 3), Stroke = faint, StrokeThickness = 1 });
        }
        var frame = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Brushes.White, StrokeThickness = 1.5 };
        Canvas.SetLeft(frame, r.X); Canvas.SetTop(frame, r.Y);
        _overlay.Children.Add(frame);
        foreach (var p in HandlePoints(r))
        {
            var h = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = Kit.Accent, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(h, p.X - 5); Canvas.SetTop(h, p.Y - 5);
            _overlay.Children.Add(h);
        }
        int w = (int)Math.Round(_sel.Width / _iw * _origW), hh = (int)Math.Round(_sel.Height / _ih * _origH);
        _size.Text = $"{w} × {hh} px  ·  " + L($"origine {_origW} × {_origH}", $"original {_origW} × {_origH}");
    }

    private Point ToImage(Point p) => new(Math.Clamp(p.X / _scale, 0, _iw), Math.Clamp(p.Y / _scale, 0, _ih));

    private void OnDown(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(_board);
        var disp = Disp;
        var pts = HandlePoints(disp);
        _start = _sel;
        _grab = ToImage(pos);
        _drag = DragKind.None;
        for (int i = 0; i < pts.Length; i++)
        {
            if (Point.Distance(pts[i], pos) <= 11)
            {
                (_l, _t, _r, _b) = HandleEdges[i];
                _drag = DragKind.Resize;
                break;
            }
        }
        if (_drag == DragKind.None)
        {
            if (disp.Contains(pos)) _drag = DragKind.Move;
            else { _drag = DragKind.New; _l = _t = false; _r = _b = true; _start = new Rect(_grab, new Size(0, 0)); }
        }
        e.Pointer.Capture(_board);
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(_board);
        var img = ToImage(pos);
        if (_drag == DragKind.None)
        {
            var disp = Disp;
            var pts = HandlePoints(disp);
            var cursor = disp.Contains(pos) ? StandardCursorType.SizeAll : StandardCursorType.Cross;
            for (int i = 0; i < pts.Length; i++)
                if (Point.Distance(pts[i], pos) <= 11)
                    cursor = i is 0 or 4 ? StandardCursorType.TopLeftCorner : i is 2 or 6 ? StandardCursorType.TopRightCorner : i is 1 or 5 ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast;
            _board.Cursor = new Cursor(cursor);
            return;
        }
        if (_drag == DragKind.Move)
        {
            double dx = img.X - _grab.X, dy = img.Y - _grab.Y;
            _sel = new Rect(Math.Clamp(_start.X + dx, 0, _iw - _start.Width), Math.Clamp(_start.Y + dy, 0, _ih - _start.Height), _start.Width, _start.Height);
        }
        else Resize(img);
        Clamp();
        Draw();
    }

    private void Resize(Point p)
    {
        const double min = 8;
        bool corner = (_l || _r) && (_t || _b);
        if (corner || _drag == DragKind.New)
        {
            double ax = _l ? _start.Right : _start.Left, ay = _t ? _start.Bottom : _start.Top;
            double w = Math.Max(min, Math.Abs(p.X - ax)), h = Math.Max(min, Math.Abs(p.Y - ay));
            int sx = p.X >= ax ? 1 : -1, sy = p.Y >= ay ? 1 : -1;
            if (_ratio is double r)
            {
                if (w / h > r) w = h * r; else h = w / r;
                double maxW = sx > 0 ? _iw - ax : ax, maxH = sy > 0 ? _ih - ay : ay;
                if (w > maxW) { w = maxW; h = w / r; }
                if (h > maxH) { h = maxH; w = h * r; }
            }
            _sel = new Rect(sx > 0 ? ax : ax - w, sy > 0 ? ay : ay - h, w, h);
            return;
        }
        double left = _start.Left, top = _start.Top, right = _start.Right, bottom = _start.Bottom;
        if (_l) left = Math.Min(p.X, right - min);
        if (_r) right = Math.Max(p.X, left + min);
        if (_t) top = Math.Min(p.Y, bottom - min);
        if (_b) bottom = Math.Max(p.Y, top + min);
        if (_ratio is double ratio)
        {
            if (_l || _r)
            {
                double w = right - left, h = Math.Min(w / ratio, _ih);
                double cy = (_start.Top + _start.Bottom) / 2;
                top = Math.Clamp(cy - h / 2, 0, _ih - h); bottom = top + h;
                w = h * ratio; if (_l) left = right - w; else right = left + w;
            }
            else
            {
                double h = bottom - top, w = Math.Min(h * ratio, _iw);
                double cx = (_start.Left + _start.Right) / 2;
                left = Math.Clamp(cx - w / 2, 0, _iw - w); right = left + w;
                h = w / ratio; if (_t) top = bottom - h; else bottom = top + h;
            }
        }
        _sel = new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void Clamp()
    {
        double x = Math.Clamp(_sel.X, 0, _iw - 1), y = Math.Clamp(_sel.Y, 0, _ih - 1);
        _sel = new Rect(x, y, Math.Clamp(_sel.Width, 1, _iw - x), Math.Clamp(_sel.Height, 1, _ih - y));
    }

    private void Reset()
    {
        _sel = new Rect(0, 0, _iw, _ih);
        _ratio = null;
        Highlight(_ratioButtons[0]);
        Draw();
    }

    private void Apply()
    {
        double fw = _sel.Width / _iw, fh = _sel.Height / _ih;
        // Locked ratio: derive the height from the width in real pixels so 1:1 really is square.
        if (_ratio is double r) fh = Math.Min(1 - _sel.Y / _ih, fw * _origW / r / _origH);
        Selection = new CropRect(_sel.X / _iw, _sel.Y / _ih, fw, fh);
        Close();
    }
}
