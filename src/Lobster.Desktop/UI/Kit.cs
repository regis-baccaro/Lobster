using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using static Lobster.Loc;

namespace Lobster;

/// <summary>Shared look for Lobster's windows (built in code, no XAML per window).</summary>
public static class Kit
{
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#E8452C"));
    public static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#1F1F23"));
    public static readonly IBrush Surface2 = new SolidColorBrush(Color.Parse("#28282D"));
    public static readonly IBrush Surface3 = new SolidColorBrush(Color.Parse("#323238"));
    public static readonly IBrush Line = new SolidColorBrush(Color.Parse("#34343B"));
    public static readonly IBrush Text = new SolidColorBrush(Color.Parse("#F2F2F4"));
    public static readonly IBrush TextDim = new SolidColorBrush(Color.Parse("#A0A0AA"));
    public static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#F0645A"));
    public static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#4ADE9B"));

    private static WindowIcon? _icon;
    public static WindowIcon? Icon
    {
        get
        {
            if (_icon != null) return _icon;
            try { _icon = new WindowIcon(AssetLoader.Open(new Uri("avares://lobster/Assets/lobster.png"))); } catch { }
            return _icon;
        }
    }

    public static Bitmap? Logo()
    {
        try { return new Bitmap(AssetLoader.Open(new Uri("avares://lobster/Assets/lobster.png"))); } catch { return null; }
    }

    public static T Styled<T>(T w) where T : Window
    {
        w.Icon = Icon;
        w.Background = new SolidColorBrush(Color.Parse("#161618"));
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w;
    }

    public static TextBlock T(string text, double size = 13, bool bold = false, IBrush? brush = null, bool wrap = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = brush ?? Text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };

    public static TextBlock Heading(string text) => new() { Text = text, Classes = { "h" } };
    public static TextBlock Dim(string text, double size = 12.5) => new() { Text = text, Classes = { "dim" }, FontSize = size };

    public static Button Btn(string text, Action click, bool accent = false, string? cls = null)
    {
        var b = new Button { Content = text };
        if (accent) b.Classes.Add("accent");
        if (cls != null) b.Classes.Add(cls);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Whether a modal Lobster dialog is open (the gesture stays quiet meanwhile).</summary>
    public static int ModalDepth;

    public static async Task<T?> ShowModal<T>(Window w, Func<T?> result)
    {
        ModalDepth++;
        try
        {
            var tcs = new TaskCompletionSource();
            w.Closed += (_, _) => tcs.TrySetResult();
            w.Topmost = true;
            w.Show();
            w.Activate();
            await tcs.Task;
            return result();
        }
        finally { ModalDepth--; }
    }
}

/// <summary>Message with buttons, or a text prompt.</summary>
public sealed class MessageWindow : Window
{
    public int Result { get; private set; } = -1;
    public string Text { get; private set; } = "";

    public MessageWindow(string title, string message, string[] buttons, string? glyph, string? input = null, Func<string, string?>? validate = null)
    {
        Kit.Styled(this);
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var root = new Grid { Margin = new Thickness(22, 20, 22, 18), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (glyph != null)
        {
            var icon = Icons.View(Icons.ByName(glyph), Kit.Accent, 26);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 16, 0);
            root.Children.Add(icon);
        }

        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        root.Children.Add(body);
        body.Children.Add(Kit.T(title, 16, bold: true));
        var msg = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Kit.TextDim, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(msg);

        TextBox? box = null;
        TextBlock? error = null;
        if (input != null)
        {
            box = new TextBox { Text = input, Margin = new Thickness(0, 14, 0, 0) };
            body.Children.Add(box);
            error = new TextBlock { Foreground = Kit.Danger, Margin = new Thickness(0, 6, 0, 0), IsVisible = false, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(error);
            Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Spacing = 8 };
        body.Children.Add(row);
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = Kit.Btn(buttons[i], () =>
            {
                if (idx == 0 && box != null && validate != null)
                {
                    string? err;
                    try { err = validate(box.Text ?? ""); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err != null) { error!.Text = err; error.IsVisible = true; return; }
                }
                Result = idx;
                Text = box?.Text ?? "";
                Close();
            }, accent: i == 0);
            b.IsDefault = i == 0;
            row.Children.Add(b);
        }
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Content = root;
    }
}
