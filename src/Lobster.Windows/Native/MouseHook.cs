using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lobster;

/// <summary>
/// Global low-level mouse hook running on its own thread, so a busy UI thread
/// can never slow down the user's mouse. Handlers must stay tiny.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    public event Action<int, int>? LeftDown;
    public event Action<int, int>? Moved;
    public event Action<int, int>? LeftUp;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private Win32.LowLevelMouseProc? _proc;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Lobster mouse hook" };
        _thread.Start();
    }

    private void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        _proc = Callback;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Error($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            return;
        }
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Win32.UnhookWindowsHookEx(_hook);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                switch ((int)wParam)
                {
                    case Win32.WM_LBUTTONDOWN: LeftDown?.Invoke(data.pt.X, data.pt.Y); break;
                    case Win32.WM_MOUSEMOVE: Moved?.Invoke(data.pt.X, data.pt.Y); break;
                    case Win32.WM_LBUTTONUP: LeftUp?.Invoke(data.pt.X, data.pt.Y); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("hook: " + ex.Message);
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
