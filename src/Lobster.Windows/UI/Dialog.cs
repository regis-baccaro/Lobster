using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lobster;

/// <summary>Dark title bars on Windows 10 20H1+ and 11.</summary>
public static class WindowTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
                int backdrop = 2; // Mica on Windows 11, ignored elsewhere
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }
            catch { }
        };
        w.Style = (Style)Application.Current.FindResource("LobsterWindow");
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Opened after a drop, Explorer still owns the foreground: take it so typing goes here.
        w.ContentRendered += (_, _) => Win32.ForceForeground(new WindowInteropHelper(w).Handle);
        try { w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/lobster.ico")); } catch { }
    }
}

/// <summary>Small dark message / prompt window, built in code.</summary>
public static class Dialog
{
    /// <summary>Shows a message with buttons; returns the clicked index (−1 if closed).</summary>
    public static int Show(string title, string message, string[] buttons, string? glyph = null)
        => Build(title, message, buttons, glyph, null, null, out _);

    public static void Error(string title, string message) =>
        Show(title, message, new[] { "OK" }, "");

    public static void Info(string title, string message) =>
        Show(title, message, new[] { "OK" }, "");

    /// <summary>Text prompt. <paramref name="validate"/> returns an error message or null.</summary>
    public static string? Ask(string title, string message, string initial, string okLabel, Func<string, string?>? validate = null)
    {
        int r = Build(title, message, new[] { okLabel, Loc.L("Annuler", "Cancel") }, null, initial, validate, out var text);
        return r == 0 ? text : null;
    }

    public static int? AskNumber(string title, string message, string initial, int min, int max)
    {
        var s = Ask(title, message, initial, "OK", t =>
            int.TryParse(t.Trim(), out var v) && v >= min && v <= max ? null : Loc.L($"Entrez un nombre entre {min} et {max}.", $"Enter a number between {min} and {max}."));
        return s != null ? int.Parse(s.Trim()) : null;
    }

    private static int Build(string title, string message, string[] buttons, string? glyph, string? input,
        Func<string, string?>? validate, out string text)
    {
        int result = -1;
        var w = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 440,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
        };
        WindowTheme.Apply(w);

        var root = new Grid { Margin = new Thickness(22, 20, 22, 18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        if (glyph != null)
        {
            root.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)w.FindResource("IconFont"), FontSize = 24,
                Foreground = (Brush)w.FindResource("Accent"), VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 16, 0)
            });
        }

        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        root.Children.Add(body);
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBox
        {
            Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Foreground = (Brush)w.FindResource("TextDim"), Padding = new Thickness(0),
            Template = PlainTextTemplate()
        });

        TextBox? box = null;
        TextBlock? error = null;
        if (input != null)
        {
            box = new TextBox { Text = input, Margin = new Thickness(0, 14, 0, 0) };
            body.Children.Add(box);
            error = new TextBlock { Foreground = (Brush)w.FindResource("Danger"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(error);
            w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        body.Children.Add(row);
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = buttons[i], Margin = new Thickness(8, 0, 0, 0), IsDefault = i == 0, IsCancel = i == buttons.Length - 1 && buttons.Length > 1 };
            if (i == 0) b.Style = (Style)w.FindResource("AccentButton");
            b.Click += (_, _) =>
            {
                if (idx == 0 && box != null && validate != null)
                {
                    string? err;
                    try { err = validate(box.Text); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err != null)
                    {
                        error!.Text = err;
                        error.Visibility = Visibility.Visible;
                        return;
                    }
                }
                result = idx;
                w.Close();
            };
            row.Children.Add(b);
        }
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.Content = root;
        w.ShowDialog();
        text = box?.Text ?? "";
        return result;
    }

    private static ControlTemplate PlainTextTemplate()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        t.VisualTree = host;
        return t;
    }
}
