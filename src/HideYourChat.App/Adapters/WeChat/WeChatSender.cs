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

    /// <summary>剪贴板+点击聚焦+Ctrl+V→Enter。必须在 UI(STA) 线程调用(剪贴板要求)。</summary>
    public async Task<SendResult> SendAsync(IntPtr hwnd, string message, double clickRatioX, double clickRatioY)
    {
        if(hwnd == IntPtr.Zero) return SendResult.Fail("wechat-paste", "未找到微信窗口");
        if(string.IsNullOrWhiteSpace(message)) return SendResult.Fail("wechat-paste","消息内容不能为空");

        // 记录原本置顶窗口
        IntPtr prevForeground = GetForegroundWindow(); // 发送前的前台窗口
        Serilog.Log.Information("【发送诊断】发送前前台窗口 hwnd={H}, overlay={O}",
            prevForeground, new System.Windows.Interop.WindowInteropHelper(
                System.Windows.Application.Current.MainWindow).Handle);
        
        // 1) 强制激活
        ForceForeground(hwnd);
        await Task.Delay(150);
        // 2) 点击输入框聚焦
        if(!GetWindowRect(hwnd, out var r)) return SendResult.Fail("wechat-paste", "读取窗口区域失败");
        int x = r.Left + (int)((r.Right - r.Left) * clickRatioX);
        int y = r.Top + (int)((r.Bottom - r.Top) * clickRatioY);

        GetCursorPos(out var saved); // 存光标，发完还原
        ClickAt(x,y);
        await Task.Delay(120);
        // 3) 打字 + 回车
        uint expected = (uint)(message.Length * 2);
        uint sent = KeyboardInput.TypeUnicode(message);
        Log.Information("【发送诊断】TypeUnicode 注入 {Sent}/{Expected}, LastError={Err}", sent, expected, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        await Task.Delay(80);     // 等内容进输入框
        KeyboardInput.Enter();

        SetCursorPos(saved.X, saved.Y); // 光标归位
        
        Log.Information("【发送诊断】准备切回: prev={P}, hwnd={H}, 相等={Eq}",
            prevForeground, hwnd, prevForeground == hwnd);
        if(prevForeground != IntPtr.Zero && prevForeground != hwnd)
        {
            await Task.Delay(120);
            RestoreForeground(prevForeground);
            // 焦点已还给工作窗口,但微信仍在 Z 序顶部压着它 → 把微信压到最底,露出工作窗口
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            Log.Warning("【发送诊断】切回被跳过! prev={P}", prevForeground);
        }

        Log.Information("微信已尝试发送: {Msg} @点击({X},{Y})", message, x, y);
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