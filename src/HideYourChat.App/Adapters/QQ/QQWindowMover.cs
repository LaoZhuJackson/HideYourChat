using System.Runtime.InteropServices;
using FlaUI.Core.WindowsAPI;

namespace HideYourChat.App.Adapters.QQ;

public static class QQWindowMover
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;}

    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private const uint MoveFlags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
    private const int SM_XVIRTUALSCREEN = 76;
    private const uint MONITORINFOF_PRIMARY = 1;

    public readonly record struct Pos(int X, int Y);

    public static Pos? GetPosition(IntPtr hwnd) => hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) ? new Pos(r.Left, r.Top) : null;

    /// <summary>移到指定位置:不改大小、不改 Z 序、不抢焦点;顺带确保非最小化。</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y)
    {
        if(hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, MoveFlags);
    }

    /// <summary>
    /// 把 QQ 藏起来,同时保证 Chromium 仍判定"可见"而继续渲染:
    ///   有副屏 → 整窗丢副屏角落(主屏完全不占);
    ///   单屏   → 屏幕最左边缘留 peek 像素一条缝兜底。
    /// </summary>
    public static void Stash(IntPtr hwnd, int peek = 8)
    {
        if(TryGetSecondaryWorkArea(out var area))
            MoveTo(hwnd, area.Left + 40, area.Top + 40); // 副屏
        else
            PeekAtScreenEdge(hwnd, peek); // 单屏
    }

    private static void PeekAtScreenEdge(IntPtr hwnd, int peek)
    {
        if(!GetWindowRect(hwnd, out var r)) return;
        int width = r.Right - r.Left;
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        MoveTo(hwnd, vLeft + peek - width, r.Top);
    }

    /// <summary>找第一块非主显示器的工作区;单屏返回 false。</summary>
    private static bool TryGetSecondaryWorkArea(out RECT workArea)
    {
        RECT found = default;
        bool got = false;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFO {cbSize = Marshal.SizeOf<MONITORINFO>()};
                if(GetMonitorInfo(hMon, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) == 0)
                {
                    found = info.rcWork;
                    got = true;
                    return false; // 拿到第一块副屏就停
                }
                return true;
            }, IntPtr.Zero);
        workArea = found;
        return got;
    }
}