using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using static Lobster.Loc;
using WinPdf = Windows.Data.Pdf;
using WinStorage = Windows.Storage;
using WinStreams = Windows.Storage.Streams;

namespace Lobster;

/// <summary>The WPF implementation of the core's UI needs.</summary>
public sealed class WpfHost : IHost
{
    public void Post(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (d.CheckAccess()) action(); else d.BeginInvoke(action);
    }

    /// <summary>Runs a modal WPF dialog on the UI thread and hands back its result.</summary>
    private static Task<T> OnUi<T>(Func<T> f)
    {
        var d = Application.Current.Dispatcher;
        if (d.CheckAccess()) return Task.FromResult(f());
        return d.InvokeAsync(f).Task;
    }

    public static string Glyph(string? name) => name switch
    {
        null => "",
        "error" => "",
        "info" => "",
        "video" => "",
        "document" => "",
        "install" => "",
        "remove" => "",
        _ => name.Length == 1 ? name : ""
    };

    public Task<int> Show(string title, string message, string[] buttons, string? glyph = null) =>
        OnUi(() => Dialog.Show(title, message, buttons, glyph == null ? null : Glyph(glyph)));

    public Task<string?> Ask(string title, string message, string initial, string okLabel, Func<string, string?>? validate = null) =>
        OnUi(() => Dialog.Ask(title, message, initial, okLabel, validate));

    public Task<CropRect?> Crop(string previewPath, int width, int height, string? note) => OnUi<CropRect?>(() =>
    {
        var w = new CropWindow(previewPath, width, height, note);
        return w.ShowDialog() == true && w.Selection is { } r ? new CropRect(r.X, r.Y, r.Width, r.Height) : null;
    });

    public Task<TrimRange?> Trim(string mediaPath, double duration, bool isVideo) => OnUi<TrimRange?>(() =>
    {
        var w = new TrimWindow(mediaPath, duration, isVideo);
        return w.ShowDialog() == true ? new TrimRange(w.Start, w.End, w.Fast) : null;
    });

    public Task<string?> PickFile(string title, string? exactName = null) => OnUi(() =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = exactName != null ? $"{exactName}|{exactName}|{L("Programmes", "Programs")} (*.exe)|*.exe" : L("Tous les fichiers", "All files") + "|*.*",
            CheckFileExists = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    });

    public Task<string?> PickFolder(string title) => OnUi(() =>
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    });

    public void SetClipboard(string text) => Post(() => Clipboard.SetText(text));

    public void Notify(string title, string text, string? revealPath = null) => Post(() => App.Current.Notify(title, text, revealPath));

    public void ShowJobs() => Post(() => App.Current.ShowJobs());

    public void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public void Reveal(string path) => Shell.Reveal(path);
}

/// <summary>Renders PDF pages with the engine built into Windows 10/11 (no extra install).</summary>
public sealed class WinPdfRenderer : IPdfRenderer
{
    public async Task Render(string src, int dpi, CancellationToken ct, Action<int, int, byte[]> onPage)
    {
        var file = await WinStorage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(src));
        WinPdf.PdfDocument doc;
        try { doc = await WinPdf.PdfDocument.LoadFromFileAsync(file); }
        catch (Exception ex)
        {
            throw new Exception(L($"Impossible d'ouvrir « {Path.GetFileName(src)} » ({ex.Message}).", $"Cannot open “{Path.GetFileName(src)}” ({ex.Message})."));
        }
        int count = (int)doc.PageCount;
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage(i);
            var opts = new WinPdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width / 96.0 * dpi)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height / 96.0 * dpi)),
                BackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };
            using var ras = new WinStreams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ras, opts);
            ras.Seek(0);
            var bytes = new byte[ras.Size];
            using (var s = ras.AsStreamForRead())
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await s.ReadAsync(bytes.AsMemory(read, bytes.Length - read), ct);
                    if (n == 0) break;
                    read += n;
                }
            }
            onPage((int)i, count, bytes);
        }
    }
}

/// <summary>XAML: Text="{l:Tr Fr='Fermer', En='Close'}" (resolved when the window loads).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Fr { get; set; } = "";
    public string En { get; set; } = "";

    public TrExtension() { }
    public TrExtension(string fr, string en) { Fr = fr; En = en; }

    public override object ProvideValue(IServiceProvider serviceProvider) => L(Fr, En);
}

public static class WpfColors
{
    public static Color ToWpf(this Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static SolidColorBrush Brush(this Rgba c)
    {
        var b = new SolidColorBrush(c.ToWpf());
        b.Freeze();
        return b;
    }
}
