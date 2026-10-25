using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static Lobster.Loc;

namespace Lobster;

/// <summary>Bottom-right activity card: progress, cancel, open results.</summary>
public sealed class JobsWindow : Window
{
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(6) };

    public JobsWindow(JobManager jobs)
    {
        Width = 400;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 460;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Title = L("Lobster — Activité", "Lobster — Activity");
        WindowStartupLocation = WindowStartupLocation.Manual;

        var header = new DockPanel { Margin = new Thickness(14, 10, 8, 6) };
        var logo = new Image { Source = Kit.Logo(), Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Kit.Btn(L("Effacer", "Clear"), () => { jobs.ClearFinished(); if (jobs.All.Count == 0) Hide(); }, cls: "ghost"));
        var close = new Button { Classes = { "ghost" }, Content = Icons.View(Icons.Get(""), Kit.TextDim, 12) };
        close.Click += (_, _) => Hide();
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var title = Kit.T("Lobster", 13, bold: true);
        title.Margin = new Thickness(8, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        var list = new ItemsControl { ItemsSource = jobs.All, Margin = new Thickness(10, 0, 6, 8), ItemTemplate = new FuncDataTemplate<Job>((job, _) => JobRow(job)) };
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(scroll);

        Content = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#F51A1A1D")), BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 3 18 0 #73000000"),
            Child = panel
        };

        jobs.Changed += () => Ui.Post(OnChanged);
        _autoHide.Tick += (_, _) =>
        {
            if (IsPointerOver || AppState.Jobs.AnyActive) return;
            _autoHide.Stop();
            Hide();
        };
        SizeChanged += (_, _) => Place();
    }

    private static Control JobRow(Job job)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var state = new ContentControl { Margin = new Thickness(0, 2, 11, 0), VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(state);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        text.Children.Add(Kit.T(job.Title, 13, bold: true));
        var status = Kit.T(job.Status, 12, brush: Kit.TextDim);
        status.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(status);
        var bar = new ProgressBar { Margin = new Thickness(0, 7, 0, 1), Maximum = 100 };
        text.Children.Add(bar);
        var error = new SelectableTextBlock { Foreground = Kit.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        text.Children.Add(error);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, -2, -4, 0) };
        Grid.SetColumn(buttons, 2);
        Button IconButton(string glyph, string tip, Action a)
        {
            var b = new Button { Classes = { "ghost" }, Content = Icons.View(Icons.Get(glyph), Kit.TextDim, 14) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => a();
            return b;
        }
        var reveal = IconButton("", L("Afficher dans le dossier", "Show in folder"), () => { if (job.Outputs.FirstOrDefault() is { } p) AppState.Host.Reveal(p); });
        var open = IconButton("", L("Ouvrir", "Open"), () => { if (job.Outputs.FirstOrDefault() is { } p) AvaloniaHost.Launch(p); });
        var cancel = IconButton("", L("Annuler", "Cancel"), () => job.Cts.Cancel());
        buttons.Children.Add(reveal); buttons.Children.Add(open); buttons.Children.Add(cancel);
        grid.Children.Add(buttons);

        void Refresh()
        {
            status.Text = job.Status;
            bar.IsVisible = job.IsActive;
            bar.IsIndeterminate = job.IsIndeterminate;
            bar.Value = job.ProgressPercent;
            error.Text = job.Error;
            error.IsVisible = job.HasError;
            reveal.IsVisible = open.IsVisible = job.HasOutputs;
            cancel.IsVisible = job.IsActive;
            var brush = job.State switch { JobState.Done => Kit.Ok, JobState.Failed => Kit.Danger, JobState.Running => Kit.Accent, _ => Kit.TextDim };
            state.Content = Icons.View(Icons.Get(job.StateGlyph), brush, 15);
        }
        Refresh();
        PropertyChangedEventHandler h = (_, _) => Dispatcher.UIThread.Post(Refresh);
        job.PropertyChanged += h;
        grid.DetachedFromVisualTree += (_, _) => job.PropertyChanged -= h;

        return new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 9), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.Parse("#26FFFFFF")), Child = grid };
    }

    public void Reveal()
    {
        if (!IsVisible) Show();
        Place();
        OnChanged();
    }

    private void OnChanged()
    {
        if (AppState.Jobs.AnyActive) _autoHide.Stop();
        else if (IsVisible) { _autoHide.Stop(); _autoHide.Start(); }
    }

    private void Place()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var wa = screen.WorkingArea;
        double s = screen.Scaling;
        Position = new PixelPoint((int)(wa.Right - Bounds.Width * s - 4 * s), (int)(wa.Bottom - Bounds.Height * s - 4 * s));
    }
}
