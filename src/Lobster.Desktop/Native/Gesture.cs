using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lobster;

/// <summary>Global "drag with the modifier key held" detection, per platform.</summary>
public interface IGestureMonitor : IDisposable
{
    /// <summary>The drag passed the threshold with the modifier held (screen pixels).</summary>
    event Action<int, int>? Triggered;
    /// <summary>The mouse button was released after a trigger.</summary>
    event Action? Released;
    /// <summary>Any left-button press (screen pixels) — used to close a pinned menu.</summary>
    event Action<int, int>? Pressed;

    bool Start();
    string Name { get; }
}

public static class GestureMonitors
{
    /// <summary>The right monitor for this session, or null (Wayland: use the bubble).</summary>
    public static IGestureMonitor? Create()
    {
        if (OperatingSystem.IsMacOS()) return new MacGestureMonitor();
        if (OperatingSystem.IsLinux() && X11GestureMonitor.SessionSupportsIt) return new X11GestureMonitor();
        return null;
    }

    internal const int Threshold = 12;

    internal static bool Modifier(bool shift, bool ctrl, bool alt) => AppState.Settings.Modifier switch
    {
        ModifierKey.Ctrl => ctrl,
        ModifierKey.Alt => alt,
        _ => shift
    };
}

/// <summary>
/// Linux / X11 sessions: polls the pointer and modifier state with XQueryPointer (cheap, no
/// extension, no root). Wayland forbids global pointer access, so it is not used there.
/// </summary>
public sealed class X11GestureMonitor : IGestureMonitor
{
    private const string LibX11 = "libX11.so.6";
    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)]
    private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);

    private const uint ShiftMask = 1, ControlMask = 4, Mod1Mask = 8, Button1Mask = 256;

    public event Action<int, int>? Triggered;
    public event Action? Released;
    public event Action<int, int>? Pressed;
    public string Name => "X11";

    private Thread? _thread;
    private volatile bool _stop;

    public static bool SessionSupportsIt =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "wayland" &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    public bool Start()
    {
        IntPtr display;
        try { display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception ex) { Log.Error("x11: " + ex.Message); return false; }
        if (display == IntPtr.Zero) return false;
        _thread = new Thread(() => Run(display)) { IsBackground = true, Name = "Lobster X11 gesture" };
        _thread.Start();
        return true;
    }

    private void Run(IntPtr display)
    {
        var root = XDefaultRootWindow(display);
        bool down = false, triggered = false;
        int sx = 0, sy = 0;
        try
        {
            while (!_stop)
            {
                if (XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out uint mask) != 0)
                {
                    bool button = (mask & Button1Mask) != 0;
                    if (button && !down)
                    {
                        down = true; triggered = false; sx = x; sy = y;
                        Pressed?.Invoke(x, y);
                    }
                    else if (!button && down)
                    {
                        down = false;
                        if (triggered) { triggered = false; Released?.Invoke(); }
                    }
                    else if (down && !triggered && AppState.Settings.GestureEnabled &&
                             (Math.Abs(x - sx) >= GestureMonitors.Threshold || Math.Abs(y - sy) >= GestureMonitors.Threshold) &&
                             GestureMonitors.Modifier((mask & ShiftMask) != 0, (mask & ControlMask) != 0, (mask & Mod1Mask) != 0))
                    {
                        triggered = true;
                        Triggered?.Invoke(x, y);
                    }
                }
                Thread.Sleep(down ? 12 : 30);
            }
        }
        catch (Exception ex) { Log.Error("x11 loop: " + ex.Message); }
        finally { try { XCloseDisplay(display); } catch { } }
    }

    public void Dispose() => _stop = true;
}

/// <summary>
/// macOS: a listen-only Quartz event tap (CGEventTapCreate). Needs the "Input Monitoring"
/// permission, which macOS asks for once.
/// </summary>
public sealed class MacGestureMonitor : IGestureMonitor
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    private delegate IntPtr TapCallback(IntPtr proxy, uint type, IntPtr evt, IntPtr userInfo);

    [DllImport(CG)] private static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong mask, TapCallback cb, IntPtr userInfo);
    [DllImport(CG)] private static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CG)] private static extern ulong CGEventGetFlags(IntPtr evt);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightListenEventAccess();
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestListenEventAccess();
    [DllImport(CF)] private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, long order);
    [DllImport(CF)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CF)] private static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CF)] private static extern void CFRunLoopRun();
    [DllImport(CF)] private static extern void CFRunLoopStop(IntPtr rl);

    private const uint SessionEventTap = 1, HeadInsert = 0, ListenOnly = 1;
    private const uint LeftDown = 1, LeftUp = 2, LeftDragged = 6, TapDisabledByTimeout = 0xFFFFFFFE, TapDisabledByUser = 0xFFFFFFFF;
    private const ulong FlagShift = 0x20000, FlagControl = 0x40000, FlagOption = 0x80000;

    public event Action<int, int>? Triggered;
    public event Action? Released;
    public event Action<int, int>? Pressed;
    public string Name => "macOS";

    /// <summary>Quartz points are also Avalonia's window coordinates on macOS: no conversion.</summary>
    private static (int x, int y) ToPixels(double x, double y) => ((int)Math.Round(x), (int)Math.Round(y));

    public static bool HasPermission
    {
        get { try { return CGPreflightListenEventAccess(); } catch { return false; } }
    }

    public static void RequestPermission()
    {
        try { CGRequestListenEventAccess(); } catch { }
    }

    private TapCallback? _cb;          // keep the delegate alive
    private IntPtr _tap, _runLoop;
    private Thread? _thread;
    private bool _down, _triggered;
    private double _sx, _sy;

    public bool Start()
    {
        var ready = new ManualResetEventSlim();
        bool ok = false;
        _thread = new Thread(() =>
        {
            try
            {
                _cb = Callback;
                ulong mask = (1UL << (int)LeftDown) | (1UL << (int)LeftUp) | (1UL << (int)LeftDragged);
                _tap = CGEventTapCreate(SessionEventTap, HeadInsert, ListenOnly, mask, _cb, IntPtr.Zero);
                if (_tap == IntPtr.Zero)
                {
                    // Only now ask macOS for "Input Monitoring"; the app retries the tap afterwards.
                    if (!HasPermission) RequestPermission();
                    ready.Set();
                    return;
                }
                var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
                _runLoop = CFRunLoopGetCurrent();
                var cf = NativeLibrary.Load(CF);
                var commonModes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFRunLoopCommonModes"));
                CFRunLoopAddSource(_runLoop, source, commonModes);
                CGEventTapEnable(_tap, true);
                ok = true;
                ready.Set();
                CFRunLoopRun();
            }
            catch (Exception ex)
            {
                Log.Error("mac tap: " + ex);
                ready.Set();
            }
        }) { IsBackground = true, Name = "Lobster macOS event tap" };
        _thread.Start();
        ready.Wait(3000);
        return ok;
    }

    private IntPtr Callback(IntPtr proxy, uint type, IntPtr evt, IntPtr userInfo)
    {
        try
        {
            if (type is TapDisabledByTimeout or TapDisabledByUser)
            {
                CGEventTapEnable(_tap, true);
                return evt;
            }
            var p = CGEventGetLocation(evt);
            switch (type)
            {
                case LeftDown:
                    _down = true; _triggered = false; _sx = p.X; _sy = p.Y;
                    var (px, py) = ToPixels(p.X, p.Y);
                    Pressed?.Invoke(px, py);
                    break;
                case LeftUp:
                    _down = false;
                    if (_triggered) { _triggered = false; Released?.Invoke(); }
                    break;
                case LeftDragged:
                    if (!_down || _triggered || !AppState.Settings.GestureEnabled) break;
                    if (Math.Abs(p.X - _sx) < GestureMonitors.Threshold && Math.Abs(p.Y - _sy) < GestureMonitors.Threshold) break;
                    var flags = CGEventGetFlags(evt);
                    if (!GestureMonitors.Modifier((flags & FlagShift) != 0, (flags & FlagControl) != 0, (flags & FlagOption) != 0)) break;
                    _triggered = true;
                    var (tx, ty) = ToPixels(p.X, p.Y);
                    Triggered?.Invoke(tx, ty);
                    break;
            }
        }
        catch (Exception ex) { Log.Error("mac tap cb: " + ex.Message); }
        return evt;
    }

    public void Dispose()
    {
        try { if (_runLoop != IntPtr.Zero) CFRunLoopStop(_runLoop); } catch { }
    }
}
