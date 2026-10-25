using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lobster;

/// <summary>Bottom-right activity card: progress, cancel, open results.</summary>
public partial class JobsWindow : Window
{
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(6) };

    public JobsWindow(JobManager jobs)
    {
        InitializeComponent();
        List.ItemsSource = jobs.All;
        Action changed = () => Ui.Post(OnChanged);
        jobs.Changed += changed;
        Closed += (_, _) => { jobs.Changed -= changed; _autoHide.Stop(); };
        _autoHide.Tick += (_, _) =>
        {
            if (IsMouseOver || App.Jobs.AnyActive) return;
            _autoHide.Stop();
            Hide();
        };
        SizeChanged += (_, _) => Place();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
        };
    }

    public void Reveal()
    {
        if (!IsVisible) Show();
        Place();
        OnChanged();
    }

    private void OnChanged()
    {
        if (App.Jobs.AnyActive) _autoHide.Stop();
        else if (IsVisible) { _autoHide.Stop(); _autoHide.Start(); }
    }

    private void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 4;
        Top = wa.Bottom - ActualHeight - 4;
    }

    private static Job? JobOf(object sender) => (sender as FrameworkElement)?.DataContext as Job;

    private void OnCancel(object sender, RoutedEventArgs e) => JobOf(sender)?.Cts.Cancel();

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        var path = JobOf(sender)?.Outputs.FirstOrDefault();
        if (path != null) Shell.Reveal(path);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var path = JobOf(sender)?.Outputs.FirstOrDefault();
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Dialog.Error(Loc.L("Ouvrir", "Open"), ex.Message); }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        App.Jobs.ClearFinished();
        if (App.Jobs.All.Count == 0) Hide();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();
}
