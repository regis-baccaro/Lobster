using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using static Lobster.Loc;

namespace Lobster;

/// <summary>The Avalonia (Linux + macOS) implementation of the core's UI needs.</summary>
public sealed class AvaloniaHost : IHost
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static Task<T> OnUi<T>(Func<Task<T>> f) =>
        Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.InvokeAsync(f);

    public Task<int> Show(string title, string message, string[] buttons, string? glyph = null) => OnUi(async () =>
    {
        var w = new MessageWindow(title, message, buttons, glyph);
        return await Kit.ShowModal(w, () => w.Result);
    });

    public Task<string?> Ask(string title, string message, string initial, string okLabel, Func<string, string?>? validate = null) => OnUi(async () =>
    {
        var w = new MessageWindow(title, message, new[] { okLabel, L("Annuler", "Cancel") }, null, initial, validate);
        return await Kit.ShowModal(w, () => w.Result == 0 ? w.Text : null);
    });

    public Task<CropRect?> Crop(string previewPath, int width, int height, string? note) => OnUi(async () =>
    {
        var w = new CropWindow(previewPath, width, height, note);
        return await Kit.ShowModal(w, () => w.Selection);
    });

    public Task<TrimRange?> Trim(string mediaPath, double duration, bool isVideo) => OnUi(async () =>
    {
        var w = new TrimWindow(mediaPath, duration, isVideo);
        return await Kit.ShowModal(w, () => w.Result);
    });

    private static TopLevel? AnyTopLevel()
    {
        var lt = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lt?.Windows.FirstOrDefault(w => w.IsVisible) ?? App.Current.Helper;
    }

    public Task<string?> PickFile(string title, string? exactName = null) => OnUi(async () =>
    {
        var top = AnyTopLevel();
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.FirstOrDefault()?.TryGetLocalPath();
    });

    public Task<string?> PickFolder(string title) => OnUi(async () =>
    {
        var top = AnyTopLevel();
        if (top == null) return null;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return dirs.FirstOrDefault()?.TryGetLocalPath();
    });

    public void SetClipboard(string text) => Post(async () =>
    {
        var top = AnyTopLevel();
        if (top?.Clipboard != null) await top.Clipboard.SetTextAsync(text);
    });

    public void Notify(string title, string text, string? revealPath = null) => Post(() => App.Current.Notify(title, text, revealPath));

    public void ShowJobs() => Post(() => App.Current.ShowJobs());

    public void OpenUrl(string url) => Launch(url);

    public void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", Directory.Exists(path) ? new[] { path } : new[] { "-R", path });
                return;
            }
            // Linux: ask the file manager to select the file (freedesktop FileManager1), else open the folder.
            var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            var dbus = Tools.FromPath("dbus-send");
            if (dbus != null && !Directory.Exists(path))
            {
                var p = Process.Start(dbus, new[]
                {
                    "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "/org/freedesktop/FileManager1",
                    "org.freedesktop.FileManager1.ShowItems", $"array:string:{uri}", "string:"
                });
                p?.WaitForExit(2000);
                if (p is { HasExited: true, ExitCode: 0 }) return;
            }
            Launch(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
        }
        catch (Exception ex) { Log.Error("reveal: " + ex.Message); }
    }

    /// <summary>Open a file, folder or URL with the desktop's default handler.</summary>
    public static void Launch(string target)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { target });
            else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false });
        }
        catch (Exception ex) { Log.Error("launch: " + ex.Message); }
    }
}
