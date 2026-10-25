using System;
using System.Diagnostics;

namespace Lobster;

/// <summary>
/// Detects "drag with the modifier key held" from raw mouse events.
/// Runs on the hook thread: only reads simple fields, never blocks.
/// </summary>
internal sealed class DragGesture
{
    private const int Threshold = 12;
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    private volatile bool _down, _triggered, _armed;
    private int _sx, _sy;

    public event Action<int, int>? Triggered;
    public event Action? Released;
    public event Action<int, int>? Pressed;

    public void Attach(MouseHook hook)
    {
        hook.LeftDown += OnDown;
        hook.Moved += OnMove;
        hook.LeftUp += (_, _) => OnUp();
    }

    private void OnDown(int x, int y)
    {
        Pressed?.Invoke(x, y);
        _down = true;
        _triggered = false;
        _sx = x; _sy = y;
        _armed = AppState.Settings.GestureEnabled && IsEligibleSource(x, y);
    }

    private void OnMove(int x, int y)
    {
        if (!_down || _triggered || !_armed) return;
        if (Math.Abs(x - _sx) < Threshold && Math.Abs(y - _sy) < Threshold) return;
        if (!ModifierHeld()) return;
        _triggered = true;
        Triggered?.Invoke(x, y);
    }

    private void OnUp()
    {
        if (!_down) return;
        _down = false;
        if (_triggered)
        {
            _triggered = false;
            Released?.Invoke();
        }
    }

    private static bool ModifierHeld() => AppState.Settings.Modifier switch
    {
        ModifierKey.Ctrl => Win32.KeyDown(Win32.VK_CONTROL),
        ModifierKey.Alt => Win32.KeyDown(Win32.VK_MENU),
        _ => Win32.KeyDown(Win32.VK_SHIFT),
    };

    private static bool IsEligibleSource(int x, int y)
    {
        try
        {
            var hwnd = Win32.WindowFromPoint(new Win32.POINT(x, y));
            if (hwnd == IntPtr.Zero) return false;
            var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            Win32.GetWindowThreadProcessId(root, out var pid);
            if (pid == OwnPid) return false;
            if (AppState.Settings.Scope == GestureScope.Everywhere) return true;
            var cls = Win32.ClassOf(root);
            return cls is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW" or "#32770";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }
}
