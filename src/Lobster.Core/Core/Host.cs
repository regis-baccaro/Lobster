using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lobster;

/// <summary>A crop selection as fractions (0..1) of the image.</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height);

/// <summary>A trim selection in seconds.</summary>
public readonly record struct TrimRange(double Start, double End, bool Fast);

/// <summary>
/// Everything the shared core needs from a user interface. Lobster.Windows (WPF) and
/// Lobster.Linux (Avalonia) each implement it; engines and the menu catalog only talk to this.
/// </summary>
public interface IHost
{
    /// <summary>Run on the UI thread (fire and forget).</summary>
    void Post(Action action);

    /// <summary>Message with buttons; returns the clicked index, −1 if closed.</summary>
    Task<int> Show(string title, string message, string[] buttons, string? glyph = null);

    /// <summary>Text prompt; <paramref name="validate"/> returns an error message or null.</summary>
    Task<string?> Ask(string title, string message, string initial, string okLabel, Func<string, string?>? validate = null);

    Task<CropRect?> Crop(string previewPath, int width, int height, string? note);
    Task<TrimRange?> Trim(string mediaPath, double duration, bool isVideo);

    Task<string?> PickFile(string title, string? exactName = null);
    Task<string?> PickFolder(string title);

    void SetClipboard(string text);
    void Notify(string title, string text, string? revealPath = null);
    void ShowJobs();
    void OpenUrl(string url);
    void Reveal(string path);
}

/// <summary>Process-wide state shared by the core and the UI.</summary>
public static class AppState
{
    public static Settings Settings { get; set; } = new();
    public static JobManager Jobs { get; set; } = null!;
    public static IHost Host { get; set; } = null!;
}

/// <summary>Convenience wrappers over <see cref="IHost"/>.</summary>
public static class Dialogs
{
    public static Task Error(string title, string message) =>
        AppState.Host.Show(title, message, new[] { "OK" }, "error");

    public static Task Info(string title, string message) =>
        AppState.Host.Show(title, message, new[] { "OK" }, "info");

    public static async Task<int?> AskNumber(string title, string message, string initial, int min, int max)
    {
        var s = await AppState.Host.Ask(title, message, initial, "OK", t =>
            int.TryParse(t.Trim(), out var v) && v >= min && v <= max
                ? null
                : Loc.L($"Entrez un nombre entre {min} et {max}.", $"Enter a number between {min} and {max}."));
        return s != null ? int.Parse(s.Trim()) : null;
    }
}

public static class Ui
{
    public static void Post(Action a) => AppState.Host?.Post(a);
}
