using System;
using System.Linq;
using Avalonia;

namespace Lobster;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppState.Settings = Settings.Load();

        // One Lobster per session: a second launch (file manager action, "Open with") forwards its files.
        if (args.Length > 0 && args[0] != "--background" && SingleInstance.Send(args, timeoutMs: 500)) return 0;
        if (args.Length == 0 && SingleInstance.Send(new[] { "--show" }, timeoutMs: 500)) return 0;

        Log.Info($"start desktop args={string.Join(' ', args)}");
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Error("fatal: " + ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .With(new X11PlatformOptions { EnableMultiTouch = false })
        .LogToTrace();
}
