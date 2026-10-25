using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Lobster;

/// <summary>Pick a start and end point on a timeline, with a live preview when Windows can play the file.</summary>
public partial class TrimWindow : Window
{
    public double Start { get; private set; }
    public double End { get; private set; }
    public bool Fast => FastBox.IsChecked == true;

    private readonly double _duration;
    private double _pos;
    private bool _playing, _canPreview;
    private enum Grab { None, Start, End, Head }
    private Grab _grab;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public TrimWindow(string path, double duration, bool isVideo)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Title = Loc.L("Découper", "Trim") + $" — {System.IO.Path.GetFileName(path)}";
        _duration = duration;
        Start = 0;
        End = duration;

        NoPreviewGlyph.Text = isVideo ? "" : "";
        NoPreviewText.Text = isVideo ? Loc.L("Chargement de l'aperçu…", "Loading preview…") : Loc.L("Audio — utilisez la lecture pour écouter la sélection.", "Audio — use play to listen to the selection.");

        Player.MediaOpened += (_, _) =>
        {
            _canPreview = true;
            if (isVideo) NoPreview.Visibility = Visibility.Collapsed;
            Player.Pause();
            Player.Volume = 0.8;
            Seek(0);
        };
        Player.MediaFailed += (_, _) =>
        {
            _canPreview = false;
            NoPreviewText.Text = Loc.L("Aperçu indisponible pour ce format.\nPlacez les repères sur la frise ou saisissez les temps.", "No preview for this format.\nPlace the markers on the timeline or type the times.");
            PlayButton.IsEnabled = false;
        };
        try
        {
            Player.Volume = 0;
            Player.Source = new Uri(path);
            Player.Play();   // needed to load the first frame
        }
        catch { PlayButton.IsEnabled = false; }

        _timer.Tick += (_, _) => Tick();
        Track.SizeChanged += (_, _) => DrawTrack();
        Track.MouseLeftButtonDown += OnTrackDown;
        Track.MouseMove += OnTrackMove;
        Track.MouseLeftButtonUp += (_, _) => { _grab = Grab.None; Track.ReleaseMouseCapture(); };
        Track.LostMouseCapture += (_, _) => _grab = Grab.None;
        Player.MediaEnded += (_, _) => { if (_playing) { Pause(); Seek(End); } };

        StartBox.LostKeyboardFocus += (_, _) => CommitBoxes();
        EndBox.LostKeyboardFocus += (_, _) => CommitBoxes();
        StartBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitBoxes(); e.Handled = true; } };
        EndBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitBoxes(); e.Handled = true; } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox) { OnPlay(this, new RoutedEventArgs()); e.Handled = true; }
        };
        Closed += (_, _) => { _timer.Stop(); Player.Close(); };

        UpdateBoxes();
        _timer.Start();
    }

    // ---------------------------------------------------------------- time text

    public static string Format(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    public static bool TryParse(string text, out double seconds)
    {
        seconds = 0;
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) return false;
        var parts = text.Split(':');
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            total = total * 60 + v;
        }
        seconds = total;
        return true;
    }

    private void UpdateBoxes()
    {
        StartBox.Text = Format(Start);
        EndBox.Text = Format(End);
        Summary.Text = Loc.L($"Sélection : {Format(End - Start)} sur {Format(_duration)}", $"Selection: {Format(End - Start)} of {Format(_duration)}");
        DrawTrack();
    }

    private void CommitBoxes()
    {
        if (TryParse(StartBox.Text, out var s)) Start = Math.Clamp(s, 0, _duration);
        if (TryParse(EndBox.Text, out var e)) End = Math.Clamp(e, 0, _duration);
        if (End < Start) (Start, End) = (End, Start);
        if (End - Start < 0.1) End = Math.Min(_duration, Start + 0.1);
        UpdateBoxes();
    }

    // ---------------------------------------------------------------- timeline

    private double X(double t) => _duration <= 0 ? 0 : t / _duration * Track.ActualWidth;
    private double T(double x) => Math.Clamp(x / Math.Max(1, Track.ActualWidth) * _duration, 0, _duration);

    private void DrawTrack()
    {
        Track.Children.Clear();
        double w = Track.ActualWidth;
        if (w <= 0) return;
        var accent = (Brush)FindResource("Accent");

        var bg = new Rectangle { Width = w, Height = 34, RadiusX = 7, RadiusY = 7, Fill = (Brush)FindResource("Surface2") };
        Canvas.SetTop(bg, 7);
        Track.Children.Add(bg);

        double xs = X(Start), xe = X(End);
        var sel = new Rectangle
        {
            Width = Math.Max(2, xe - xs), Height = 34, RadiusX = 6, RadiusY = 6,
            Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xE8, 0x45, 0x2C)), Stroke = accent, StrokeThickness = 1.5
        };
        Canvas.SetLeft(sel, xs); Canvas.SetTop(sel, 7);
        Track.Children.Add(sel);

        foreach (var x in new[] { xs, xe })
        {
            var h = new Rectangle { Width = 10, Height = 44, RadiusX = 4, RadiusY = 4, Fill = accent };
            Canvas.SetLeft(h, x - 5); Canvas.SetTop(h, 2);
            Track.Children.Add(h);
        }

        var head = new Rectangle { Width = 2, Height = 48, Fill = Brushes.White };
        Canvas.SetLeft(head, X(_pos) - 1);
        Track.Children.Add(head);
    }

    private void OnTrackDown(object sender, MouseButtonEventArgs e)
    {
        double x = e.GetPosition(Track).X;
        if (Math.Abs(x - X(Start)) <= 9) _grab = Grab.Start;
        else if (Math.Abs(x - X(End)) <= 9) _grab = Grab.End;
        else { _grab = Grab.Head; Seek(T(x)); }
        Track.CaptureMouse();
        e.Handled = true;
    }

    private void OnTrackMove(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(Track).X;
        if (_grab == Grab.None)
        {
            Track.Cursor = Math.Abs(x - X(Start)) <= 9 || Math.Abs(x - X(End)) <= 9 ? Cursors.SizeWE : Cursors.Hand;
            return;
        }
        double t = T(x);
        switch (_grab)
        {
            case Grab.Start: Start = Math.Min(t, End - 0.1); Seek(Start); break;
            case Grab.End: End = Math.Max(t, Start + 0.1); Seek(End); break;
            case Grab.Head: Seek(t); break;
        }
        UpdateBoxes();
    }

    // ---------------------------------------------------------------- playback

    private void Seek(double t)
    {
        _pos = Math.Clamp(t, 0, _duration);
        if (_canPreview) Player.Position = TimeSpan.FromSeconds(_pos);
        Clock.Text = Format(_pos);
        DrawTrack();
    }

    private void Tick()
    {
        if (!_playing) return;
        _pos = Player.Position.TotalSeconds;
        if (_pos >= End) { Pause(); Seek(End); return; }
        Clock.Text = Format(_pos);
        DrawTrack();
    }

    private void Pause()
    {
        _playing = false;
        if (_canPreview) Player.Pause();
        PlayGlyph.Text = "";
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (!_canPreview) return;
        if (_playing) { Pause(); return; }
        if (_pos < Start || _pos >= End - 0.05) Seek(Start);
        Player.Play();
        _playing = true;
        PlayGlyph.Text = "";
    }

    private void OnStartHere(object sender, RoutedEventArgs e)
    {
        Start = Math.Min(_pos, End - 0.1);
        UpdateBoxes();
    }

    private void OnEndHere(object sender, RoutedEventArgs e)
    {
        End = Math.Max(_pos, Start + 0.1);
        UpdateBoxes();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        CommitBoxes();
        Pause();
        DialogResult = true;
    }
}
