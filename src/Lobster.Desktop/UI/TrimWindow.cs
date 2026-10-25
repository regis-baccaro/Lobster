using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using static Lobster.Loc;

namespace Lobster;

/// <summary>
/// Pick a start and end on a timeline. Video: a frame preview follows the playhead (rendered by ffmpeg).
/// Audio and video: the waveform is drawn under the timeline.
/// </summary>
public sealed class TrimWindow : Window
{
    public TrimRange? Result { get; private set; }

    private readonly string _path;
    private readonly double _duration;
    private readonly bool _isVideo;
    private double _start, _end, _pos;
    private enum Grab { None, Start, End, Head }
    private Grab _grab;

    private readonly Canvas _track = new() { Height = 56, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
    private readonly Image _wave = new() { Stretch = Stretch.Fill, Opacity = 0.9, IsHitTestVisible = false };
    private readonly Image _frame = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _placeholder = Kit.Dim("", 13);
    private readonly TextBox _startBox = new() { Width = 110, FontFamily = new FontFamily("monospace") };
    private readonly TextBox _endBox = new() { Width = 110, FontFamily = new FontFamily("monospace") };
    private readonly TextBlock _clock = Kit.T("0:00.0", 14);
    private readonly TextBlock _summary = Kit.Dim("");
    private readonly CheckBox _fast = new() { Content = L("Rapide (sans réencodage)", "Fast (no re-encoding)") };
    private CancellationTokenSource? _frameCts;
    private readonly DispatcherTimer _frameDebounce;

    public TrimWindow(string path, double duration, bool isVideo)
    {
        Kit.Styled(this);
        _path = path; _duration = duration; _isVideo = isVideo;
        _start = 0; _end = duration;
        Title = L("Découper", "Trim") + $" — {System.IO.Path.GetFileName(path)}";
        Width = 900; Height = isVideo ? 660 : 380; MinWidth = 620; MinHeight = 320;

        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };

        // Bottom bar
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Kit.Btn(L("Annuler", "Cancel"), Close));
        var apply = Kit.Btn(L("Découper", "Trim"), Apply, accent: true);
        apply.IsDefault = true;
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        ToolTip.SetTip(_fast, L("Instantané et sans perte, mais la coupe se cale sur l'image clé la plus proche.",
                                "Instant and lossless, but the cut snaps to the nearest keyframe."));
        _fast.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_fast, Dock.Left);
        bottom.Children.Add(_fast);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Margin = new Thickness(18, 0, 0, 0);
        bottom.Children.Add(_summary);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // Time fields
        var times = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.Margin = new Thickness(0, 0, 22, 6);
        _clock.FontFamily = new FontFamily("monospace");
        times.Children.Add(_clock);
        times.Children.Add(Field(L("Début", "Start"), _startBox));
        times.Children.Add(Kit.Btn(L("⟦ Ici", "⟦ Here"), () => { _start = Math.Min(_pos, _end - 0.1); Update(); }, cls: "chip"));
        times.Children.Add(new Border { Width = 18 });
        times.Children.Add(Field(L("Fin", "End"), _endBox));
        times.Children.Add(Kit.Btn(L("Ici ⟧", "Here ⟧"), () => { _end = Math.Max(_pos, _start + 0.1); Update(); }, cls: "chip"));
        DockPanel.SetDock(times, Dock.Bottom);
        root.Children.Add(times);

        // Timeline
        var trackHost = new Panel { Margin = new Thickness(0, 16, 0, 0), Height = 56 };
        trackHost.Children.Add(new Border { Background = Kit.Surface2, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 6), ClipToBounds = true, Child = _wave });
        trackHost.Children.Add(_track);
        DockPanel.SetDock(trackHost, Dock.Bottom);
        root.Children.Add(trackHost);

        // Preview
        var preview = new Panel();
        preview.Children.Add(_frame);
        _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.TextAlignment = TextAlignment.Center;
        _placeholder.Text = isVideo ? L("Chargement de l'aperçu…", "Loading preview…")
                                    : L("Placez les repères sur la frise ou saisissez les temps.", "Place the markers on the timeline or type the times.");
        preview.Children.Add(_placeholder);
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#0D0D0F")), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = preview, IsVisible = true });

        Content = root;

        _frameDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _frameDebounce.Tick += (_, _) => { _frameDebounce.Stop(); _ = LoadFrame(_pos); };

        _track.SizeChanged += (_, _) => DrawTrack();
        _track.PointerPressed += OnTrackDown;
        _track.PointerMoved += OnTrackMove;
        _track.PointerReleased += (_, _) => _grab = Grab.None;
        _track.PointerCaptureLost += (_, _) => _grab = Grab.None;
        foreach (var box in new[] { _startBox, _endBox })
        {
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        }
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += async (_, _) =>
        {
            Update();
            await LoadWave();
            if (_isVideo) await LoadFrame(0);
        };
        Closed += (_, _) => _frameCts?.Cancel();
    }

    private static Control Field(string label, TextBox box)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 6, 6) };
        var t = Kit.Dim(label, 13);
        t.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(t);
        sp.Children.Add(box);
        return sp;
    }

    // ---------------------------------------------------------------- time text

    public static string Format(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        text = (text ?? "").Trim().Replace(',', '.');
        if (text.Length == 0) return false;
        double total = 0;
        foreach (var p in text.Split(':'))
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            total = total * 60 + v;
        }
        seconds = total;
        return true;
    }

    private void Update()
    {
        _startBox.Text = Format(_start);
        _endBox.Text = Format(_end);
        _summary.Text = L($"Sélection : {Format(_end - _start)} sur {Format(_duration)}", $"Selection: {Format(_end - _start)} of {Format(_duration)}");
        DrawTrack();
    }

    private void Commit()
    {
        if (TryParse(_startBox.Text, out var s)) _start = Math.Clamp(s, 0, _duration);
        if (TryParse(_endBox.Text, out var e)) _end = Math.Clamp(e, 0, _duration);
        if (_end < _start) (_start, _end) = (_end, _start);
        if (_end - _start < 0.1) _end = Math.Min(_duration, _start + 0.1);
        Update();
    }

    // ---------------------------------------------------------------- timeline

    private double X(double t) => _duration <= 0 ? 0 : t / _duration * _track.Bounds.Width;
    private double Tm(double x) => Math.Clamp(x / Math.Max(1, _track.Bounds.Width) * _duration, 0, _duration);

    private void DrawTrack()
    {
        _track.Children.Clear();
        double w = _track.Bounds.Width;
        if (w <= 0) return;
        double xs = X(_start), xe = X(_end);
        var dim = new SolidColorBrush(Color.FromArgb(0x99, 0x0D, 0x0D, 0x0F));
        var left = new Rectangle { Width = Math.Max(0, xs), Height = 44, Fill = dim };
        Canvas.SetTop(left, 6);
        var right = new Rectangle { Width = Math.Max(0, w - xe), Height = 44, Fill = dim };
        Canvas.SetLeft(right, xe); Canvas.SetTop(right, 6);
        var sel = new Rectangle
        {
            Width = Math.Max(2, xe - xs), Height = 44, RadiusX = 6, RadiusY = 6,
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xE8, 0x45, 0x2C)), Stroke = Kit.Accent, StrokeThickness = 1.5
        };
        Canvas.SetLeft(sel, xs); Canvas.SetTop(sel, 6);
        _track.Children.Add(left); _track.Children.Add(right); _track.Children.Add(sel);
        foreach (var x in new[] { xs, xe })
        {
            var h = new Rectangle { Width = 10, Height = 56, RadiusX = 4, RadiusY = 4, Fill = Kit.Accent };
            Canvas.SetLeft(h, x - 5);
            _track.Children.Add(h);
        }
        var head = new Rectangle { Width = 2, Height = 56, Fill = Brushes.White };
        Canvas.SetLeft(head, X(_pos) - 1);
        _track.Children.Add(head);
    }

    private void OnTrackDown(object? sender, PointerPressedEventArgs e)
    {
        double x = e.GetPosition(_track).X;
        if (Math.Abs(x - X(_start)) <= 9) _grab = Grab.Start;
        else if (Math.Abs(x - X(_end)) <= 9) _grab = Grab.End;
        else { _grab = Grab.Head; Seek(Tm(x)); }
        e.Pointer.Capture(_track);
        e.Handled = true;
    }

    private void OnTrackMove(object? sender, PointerEventArgs e)
    {
        double x = e.GetPosition(_track).X;
        if (_grab == Grab.None)
        {
            _track.Cursor = new Cursor(Math.Abs(x - X(_start)) <= 9 || Math.Abs(x - X(_end)) <= 9 ? StandardCursorType.SizeWestEast : StandardCursorType.Hand);
            return;
        }
        double t = Tm(x);
        switch (_grab)
        {
            case Grab.Start: _start = Math.Min(t, _end - 0.1); Seek(_start); break;
            case Grab.End: _end = Math.Max(t, _start + 0.1); Seek(_end); break;
            case Grab.Head: Seek(t); break;
        }
        Update();
    }

    private void Seek(double t)
    {
        _pos = Math.Clamp(t, 0, _duration);
        _clock.Text = Format(_pos);
        DrawTrack();
        if (_isVideo) { _frameDebounce.Stop(); _frameDebounce.Start(); }
    }

    // ---------------------------------------------------------------- previews (ffmpeg)

    private async Task LoadFrame(double at)
    {
        _frameCts?.Cancel();
        var cts = _frameCts = new CancellationTokenSource();
        try
        {
            var png = await FfmpegEngine.ExtractFrame(_path, Math.Min(at, Math.Max(0, _duration - 0.05)), cts.Token);
            if (cts.IsCancellationRequested) return;
            _frame.Source = new Bitmap(png);
            _placeholder.IsVisible = false;
            OutputPaths.TryDelete(System.IO.Path.GetDirectoryName(png));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _placeholder.Text = L("Aperçu indisponible pour ce format.", "No preview for this format.");
            Log.Error("trim frame: " + ex.Message);
        }
    }

    private async Task LoadWave()
    {
        try
        {
            var ff = Tools.Ffmpeg;
            if (ff == null) return;
            var dir = OutputPaths.TempDir();
            var png = System.IO.Path.Combine(dir, "wave.png");
            var r = await ProcessRunner.Run(ff, new[]
            {
                "-hide_banner", "-nostdin", "-y", "-i", _path, "-filter_complex",
                "aformat=channel_layouts=mono,showwavespic=s=1600x88:colors=#E8452C", "-frames:v", "1", png
            }, CancellationToken.None);
            if (r.ExitCode == 0 && File.Exists(png)) _wave.Source = new Bitmap(png);
        }
        catch (Exception ex) { Log.Error("trim wave: " + ex.Message); }
    }

    private void Apply()
    {
        Commit();
        Result = new TrimRange(_start, _end, _fast.IsChecked == true);
        Close();
    }
}
