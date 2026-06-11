using System.Runtime.InteropServices;
using HideYourChat.App.Core;
using Serilog;

namespace HideYourChat.App.Adapters.WeChat;

public sealed class WeChatSender
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private const int SW_RESTORE = 9;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02, MOUSEEVENTF_LEFTUP = 0x04;

    /// <summary>
    /// 向微信窗口发送消息的完整流程：激活窗口 → 点击输入框聚焦 → Unicode 逐字注入 → 回车发送 → 恢复前台。
    /// 必须在 UI(STA) 线程调用。
    /// </summary>
    /// <param name="hwnd">微信聊天窗口句柄</param>
    /// <param name="message">要发送的消息文本</param>
    /// <param name="clickRatioX">输入框点击位置 X 比例（相对于窗口宽度）</param>
    /// <param name="clickRatioY">输入框点击位置 Y 比例（相对于窗口高度）</param>
    public async Task<SendResult> SendAsync(IntPtr hwnd, string message, double clickRatioX, double clickRatioY)
    {
        // ── 前置校验 ──
        if (hwnd == IntPtr.Zero)
            return SendResult.Fail("wechat-paste", "未找到微信窗口");
        if (string.IsNullOrWhiteSpace(message))
            return SendResult.Fail("wechat-paste", "消息内容不能为空");

        // ── 阶段 1：保存当前前台窗口，用于发送完成后恢复 ──
        IntPtr prevForeground = GetForegroundWindow();

        // ── 阶段 2：激活微信窗口 ──
        // AttachThreadInput 将当前线程的消息队列与前台线程关联，
        // 从而绕过 SetForegroundWindow 对非前台进程的权限限制。
        ForceForeground(hwnd);
        await Task.Delay(150); // 等待窗口激活完成

        // ── 阶段 3：点击输入框区域，确保键盘焦点落在微信输入框 ──
        if (!GetWindowRect(hwnd, out var rect))
            return SendResult.Fail("wechat-paste", "读取窗口区域失败");

        int clickX = rect.Left + (int)((rect.Right - rect.Left) * clickRatioX);
        int clickY = rect.Top + (int)((rect.Bottom - rect.Top) * clickRatioY);

        GetCursorPos(out var savedCursor); // 保存当前光标位置，阶段 5 还原
        ClickAt(clickX, clickY);
        await Task.Delay(120); // 等待鼠标事件被窗口处理

        // ── 阶段 4：Unicode 逐字注入消息，然后回车发送 ──
        // 使用 KEYEVENTF_UNICODE 直接注入字符（而非剪贴板 Ctrl+V），
        // 原因：不污染用户剪贴板、兼容更多输入法场景、每字符有明确的按下/抬起事件。
        KeyboardInput.TypeUnicode(message);
        await Task.Delay(80); // 等待消息内容完全进入输入框
        KeyboardInput.Enter();

        // ── 阶段 5：还原光标位置 ──
        SetCursorPos(savedCursor.X, savedCursor.Y);

        // ── 阶段 6：恢复原前台窗口，并将微信窗口压到底部 ──
        // 如果发送前的前台窗口不是微信本身（且仍有效），则将其恢复为前台。
        // SetWindowPos(HWND_BOTTOM) 将微信压到 Z 序最底层，
        // 避免微信窗口遮挡用户原本正在操作的工作窗口。
        if (prevForeground != IntPtr.Zero && prevForeground != hwnd)
        {
            await Task.Delay(120); // 等待回车事件被微信处理
            RestoreForeground(prevForeground);
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        Log.Information("微信消息已发送: {Msg} @ 点击({X},{Y})", message, clickX, clickY);
        return SendResult.Ok("wechat-paste");
    }

    private static bool ForceForeground(IntPtr hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint curThread = GetCurrentThreadId();

        if(fgThread != curThread) AttachThreadInput(curThread, fgThread, true);
        BringWindowToTop(hwnd);
        bool ok = SetForegroundWindow(hwnd);
        if(fgThread != curThread) AttachThreadInput(curThread, fgThread, false);
        return ok;
    }

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0,0,0, IntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
    }

    private static bool RestoreForeground(IntPtr target)
    {
        if(target == IntPtr.Zero) return false;

        uint curThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(target, out _);
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

        if(fgThread != curThread) AttachThreadInput(curThread, fgThread, true);
        if(targetThread != curThread) AttachThreadInput(curThread, targetThread, true);

        ShowWindow(target, SW_RESTORE);
        BringWindowToTop(target);
        bool ok = SetForegroundWindow(target);

        if (targetThread != curThread) AttachThreadInput(curThread, targetThread, false);
        if (fgThread != curThread)     AttachThreadInput(curThread, fgThread, false);
        return ok;
    }
}