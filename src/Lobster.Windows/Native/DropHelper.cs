using System;
using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Lobster;

[ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    void DragEnter(IntPtr hwndTarget, ComDataObject dataObject, ref Win32.POINT pt, int effect);
    void DragLeave();
    void DragOver(ref Win32.POINT pt, int effect);
    void Drop(ComDataObject dataObject, ref Win32.POINT pt, int effect);
    void Show(bool show);
}

[ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
internal class DragDropHelperClass { }

/// <summary>Keeps Explorer's drag thumbnail visible while hovering our window.</summary>
internal sealed class DropHelper
{
    private readonly IDropTargetHelper? _helper;

    public DropHelper()
    {
        try { _helper = (IDropTargetHelper)new DragDropHelperClass(); }
        catch { _helper = null; }
    }

    public void Enter(IntPtr hwnd, object data, int effect)
    {
        if (_helper == null || data is not ComDataObject com) return;
        try { var p = Win32.Cursor(); _helper.DragEnter(hwnd, com, ref p, effect); } catch { }
    }

    public void Over(int effect)
    {
        if (_helper == null) return;
        try { var p = Win32.Cursor(); _helper.DragOver(ref p, effect); } catch { }
    }

    public void Leave()
    {
        if (_helper == null) return;
        try { _helper.DragLeave(); } catch { }
    }

    public void Drop(object data, int effect)
    {
        if (_helper == null || data is not ComDataObject com) return;
        try { var p = Win32.Cursor(); _helper.Drop(com, ref p, effect); } catch { }
    }
}
