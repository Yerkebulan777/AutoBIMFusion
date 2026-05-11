using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Windows;

namespace SioForgeCAD.Commun.Mist;

internal static class User32PInvoke
{
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    internal static bool SetAsForeground(this IntPtr hWnd)
    {
        return SetForegroundWindow(hWnd);
    }

    internal static bool SetAsForeground(this Window win)
    {
        return win.Handle.SetAsForeground();
    }


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(ref Win32Point pt);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Point
    {
        public Int32 X;
        public Int32 Y;
    }
}
