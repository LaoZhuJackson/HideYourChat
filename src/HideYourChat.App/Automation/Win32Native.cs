using System.Runtime.InteropServices;
using System.Text;

namespace HideYourChat.App.Automation;

public static class Win32Native
{
    private const int GwlExStyle = -20;           // 获取扩展窗口样式的索引
    private const long WsExTransparent = 0x00000020L;  // 透明样式（点击穿透）
    private const long WsExLayered = 0x00080000L;     // 分层窗口样式（支持透明）

    public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    public static void SetWindowClickThrough(IntPtr hwnd, bool enabled)
    {
        if(hwnd == IntPtr.Zero) return;
        // 1. 获取当前扩展样式
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        // 2. 强制添加 Layered 样式（支持透明操作）
        exStyle |= WsExLayered;
        // 3. 根据 enabled 参数添加或移除透明样式
        if(enabled) exStyle |= WsExTransparent;
        else exStyle &= ~WsExTransparent;
        // 4. 应用新样式
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(exStyle));
    }
}