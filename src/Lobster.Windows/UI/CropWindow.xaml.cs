using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Lobster;

/// <summary>Interactive crop: drag to draw, move or resize a frame; optional fixed ratio.</summary>
public partial class CropWindow : Window
{
    /// <summary>Selection as fractions of the image (0..1).</summary>
    public Rect? Selection { get; private set; }

    private readonly double _iw, _ih;          // preview pixel size
    private readonly int _origW, _origH;       // real media size, for the label
    private double _scale = 1;
    private Rect _sel;
    private double? _ratio;                    // width / height

    private enum Drag { None, Move, Resize, New }
    private Drag _drag;
    private bool _l, _t, _r, _b;               // edges being dragged
    private Point _grab;
    private Rect _start;

    public CropWindow(string previewPath, int originalWidth, int originalHeight, string? note = null)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(previewPath);
        bmp.EndInit();
        bmp.Freeze();
        Pic.Source = bmp;
        _iw = bmp.PixelWidth;
        _ih = bmp.PixelHeight;
        _origW = originalWidth > 0 ? originalWidth : bmp.PixelWidth;
        _origH = originalHeight > 0 ? originalHeight : bmp.PixelHeight;
        Note.Text = note ?? "";

        foreach (var (label, ratio) in new (string, double?)[]
                 {
                     (Loc.L("Libre", "Free"), null), ("Original", _iw / _ih), ("1:1", 1), ("4:3", 4.0 / 3), ("3:2", 1.5),
                     ("16:9", 16.0 / 9), ("9:16", 9.0 / 16), ("4:5", 0.8)
                 })
        {
            var b = new Button { Content = label, Style = (Style)FindResource("ChipButton") };
            b.Click += (_, _) => SetRatio(ratio, b);
            Ratios.Children.Add(b);
        }
        Highlight((Button)Ratios.Children[0]);

        _sel = new Rect(_iw * 0.08, _ih * 0.08, _iw * 0.84, _ih * 0.84);
        Host.SizeChanged += (_, _) => Layout();
        Board.MouseLeftButtonDown += OnDown;
        Board.MouseMove += OnMove;
        Board.MouseLeftButtonUp += (_, _) => { _drag = Drag.None; Board.ReleaseMouseCapture(); };
        Board.LostMouseCapture += (_, _) => _drag = Drag.None;
        Loaded += (_, _) => Layout();
    }

    private void Highlight(Button active)
    {
        foreach (Button b in Ratios.Children)
        {
            b.Background = b == active ? (Brush)FindResource("Accent") : (Brush)FindResource("Surface2");
            b.Foreground = b == active ? Brushes.White : (Brush)FindResource("Text");
        }
    }

    private void SetRatio(double? ratio, Button source)
    {
        _ratio = ratio;
        Highlight(source);
        if (ratio is double r)
        {
            // Largest rect with this ratio, centered on the current selection, inside the image.
            double w = _iw, h = w / r;
            if (h > _ih) { h = _ih; w = h * r; }
            w *= 0.9; h *= 0.9;
            var c = new Point(_sel.X + _sel.Width / 2, _sel.Y + _sel.Height / 2);
            double x = Math.Clamp(c.X - w / 2, 0, _iw - w), y = Math.Clamp(c.Y - h / 2, 0, _ih - h);
            _sel = new Rect(x, y, w, h);
        }
        Draw();
    }

    private void Layout()
    {
        double aw = Host.ActualWidth, ah = Host.ActualHeight;
        if (aw <= 0 || ah <= 0) return;
        _scale = Math.Min(aw / _iw, ah / _ih);
        Board.Width = _iw * _scale;
        Board.Height = _ih * _scale;
        Pic.Width = Board.Width;
        Pic.Height = Board.Height;
        Draw();
    }

    private void Draw()
    {
        var s = _scale;
        var full = new Rect(0, 0, _iw * s, _ih * s);
        var r = new Rect(_sel.X * s, _sel.Y * s, _sel.Width * s, _sel.Height * s);
        Shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(r));
        Canvas.SetLeft(Frame, r.X); Canvas.SetTop(Frame, r.Y);
        Frame.Width = r.Width; Frame.Height = r.Height;

        Thirds.Children.Clear();
        var line = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        for (int i = 1; i <= 2; i++)
        {
            Thirds.Children.Add(new Line { X1 = r.X + r.Width * i / 3, X2 = r.X + r.Width * i / 3, Y1 = r.Y, Y2 = r.Bottom, Stroke = line, StrokeThickness = 1 });
            Thirds.Children.Add(new Line { X1 = r.X, X2 = r.Right, Y1 = r.Y + r.Height * i / 3, Y2 = r.Y + r.Height * i / 3, Stroke = line, StrokeThickness = 1 });
        }

        Handles.Children.Clear();
        foreach (var p in HandlePoints(r))
        {
            var h = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = (Brush)FindResource("Accent"), StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(h, p.X - 5); Canvas.SetTop(h, p.Y - 5);
            Handles.Children.Add(h);
        }

        int w = (int)Math.Round(_sel.Width / _iw * _origW), hgt = (int)Math.Round(_sel.Height / _ih * _origH);
        SizeLabel.Text = $"{w} × {hgt} px  ·  " + Loc.L($"origine {_origW} × {_origH}", $"original {_origW} × {_origH}");
    }

    private static Point[] HandlePoints(Rect r) => new[]
    {
        new Point(r.Left, r.Top), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Top),
        new Point(r.Right, r.Top + r.Height / 2), new Point(r.Right, r.Bottom), new Point(r.Left + r.Width / 2, r.Bottom),
        new Point(r.Left, r.Bottom), new Point(r.Left, r.Top + r.Height / 2)
    };

    // handle index → edges (L, T, R, B)
    private static readonly (bool l, bool t, bool r, bool b)[] HandleEdges =
    {
        (true, true, false, false), (false, true, false, false), (false, true, true, false), (false, false, true, false),
        (false, false, true, true), (false, false, false, true), (true, false, false, true), (true, false, false, false)
    };

    private Point ToImage(Point p) => new(Math.Clamp(p.X / _scale, 0, _iw), Math.Clamp(p.Y / _scale, 0, _ih));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(Board);
        var disp = new Rect(_sel.X * _scale, _sel.Y * _scale, _sel.Width * _scale, _sel.Height * _scale);
        var pts = HandlePoints(disp);
        _start = _sel;
        _grab = ToImage(pos);
        _drag = Drag.None;
        for (int i = 0; i < pts.Length; i++)
        {
            if ((pts[i] - pos).Length <= 11)
            {
                (_l, _t, _r, _b) = HandleEdges[i];
                _drag = Drag.Resize;
                break;
            }
        }
        if (_drag == Drag.None)
        {
            if (disp.Contains(pos)) _drag = Drag.Move;
            else
            {
                _drag = Drag.New;
                _l = _t = false; _r = _b = true;
                _start = new Rect(_grab, new Size(0, 0));
            }
        }
        Board.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Board);
        var img = ToImage(pos);

        if (_drag == Drag.None)
        {
            var disp = new Rect(_sel.X * _scale, _sel.Y * _scale, _sel.Width * _scale, _sel.Height * _scale);
            var pts = HandlePoints(disp);
            Cursor cursor = disp.Contains(pos) ? Cursors.SizeAll : Cursors.Cross;
            for (int i = 0; i < pts.Length; i++)
                if ((pts[i] - pos).Length <= 11)
                    cursor = i is 0 or 4 ? Cursors.SizeNWSE : i is 2 or 6 ? Cursors.SizeNESW : i is 1 or 5 ? Cursors.SizeNS : Cursors.SizeWE;
            Board.Cursor = cursor;
            return;
        }

        if (_drag == Drag.Move)
        {
            double dx = img.X - _grab.X, dy = img.Y - _grab.Y;
            _sel = new Rect(Math.Clamp(_start.X + dx, 0, _iw - _start.Width), Math.Clamp(_start.Y + dy, 0, _ih - _start.Height), _start.Width, _start.Height);
            Draw();
            return;
        }

        Resize(img);
        ClampSelection();
        Draw();
    }

    private void Resize(Point p)
    {
        const double min = 8;
        bool corner = (_l || _r) && (_t || _b);
        if (corner || _drag == Drag.New)
        {
            // Anchor is the opposite corner.
            double ax = _l ? _start.Right : _start.Left;
            double ay = _t ? _start.Bottom : _start.Top;
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
                double w = right - left, h = w / ratio;
                if (h > _ih) { h = _ih; w = h * ratio; if (_l) left = right - w; else right = left + w; }
                double cy = (_start.Top + _start.Bottom) / 2;
                top = Math.Clamp(cy - h / 2, 0, _ih - h); bottom = top + h;
            }
            else
            {
                double h = bottom - top, w = h * ratio;
                if (w > _iw) { w = _iw; h = w / ratio; if (_t) top = bottom - h; else bottom = top + h; }
                double cx = (_start.Left + _start.Right) / 2;
                left = Math.Clamp(cx - w / 2, 0, _iw - w); right = left + w;
            }
        }
        _sel = new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void ClampSelection()
    {
        double x = Math.Clamp(_sel.X, 0, _iw - 1), y = Math.Clamp(_sel.Y, 0, _ih - 1);
        double w = Math.Clamp(_sel.Width, 1, _iw - x), h = Math.Clamp(_sel.Height, 1, _ih - y);
        _sel = new Rect(x, y, w, h);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _sel = new Rect(0, 0, _iw, _ih);
        _ratio = null;
        Highlight((Button)Ratios.Children[0]);
        Draw();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        double fw = _sel.Width / _iw, fh = _sel.Height / _ih;
        // Locked ratio: derive the height from the width in real pixels so 1:1 really is square.
        if (_ratio is double r) fh = Math.Min(1 - _sel.Y / _ih, fw * _origW / r / _origH);
        Selection = new Rect(_sel.X / _iw, _sel.Y / _ih, fw, fh);
        DialogResult = true;
    }
}
