using System.Runtime.InteropServices;
using FlaUI.Core.WindowsAPI;

namespace HideYourChat.App.Adapters.WeChat;

/// <summary>用 SendInput 模拟键盘:Unicode 逐字键入、粘贴(Ctrl+V)、回车。</summary>
public static class KeyboardInput
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;     // ← 最大的成员,union 尺寸由它决定
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56, VK_RETURN = 0x0D;

    // 构造按键结构体
    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
    };

    private static uint Send(params INPUT[] inputs)
    {
        int sz = Marshal.SizeOf<INPUT>();
        uint ret = SendInput((uint)inputs.Length, inputs, sz);
        if(ret == 0)
            Serilog.Log.Warning("SendInput failed: 0 events injected, sizeof(INPUT)={Sz}, LastError={E}", sz, Marshal.GetLastWin32Error());
        return ret;
    }
    public static void Paste() => Send(Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true));
    public static void Enter() => Send(Key(VK_RETURN, false), Key(VK_RETURN, true));

    public static uint TypeUnicode(string text)
    {
        var list = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            list.Add(UnicodeKey(c, false));
            list.Add(UnicodeKey(c, true));
        }
        if (list.Count == 0) return 0;
        return SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT UnicodeKey(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } }
    };
}