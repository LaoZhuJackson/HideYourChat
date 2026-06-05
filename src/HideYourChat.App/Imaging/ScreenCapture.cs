using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 通用窗口截图工具：按进程名找窗口、PrintWindow 截图、按比例裁剪区域。
/// 不绑定任何具体应用，任何需要截图的适配器都可复用。
/// </summary>
public sealed class ScreenCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    /// <summary>
    /// 在给定的候选进程名里，找一个带可见主窗口的进程，返回其窗口句柄。
    /// 多开/多进程时取内存占用最大的（通常是主界面）。找不到返回 IntPtr.Zero。
    /// </summary>
    public IntPtr FindMainWindowHandle(IEnumerable<string> candidateProcessNames)
    {
        Process? best = null;
        foreach (var name in candidateProcessNames)
        {
            foreach(var p in Process.GetProcessesByName(name))
            {
                if(p.MainWindowHandle != IntPtr.Zero &&
                (best is null || p.WorkingSet64 > best.WorkingSet64))
                {
                    best?.Dispose();
                    best = p;
                }
                else p.Dispose();
            }
        }
        var handle = best?.MainWindowHandle ?? IntPtr.Zero;
        best?.Dispose();
        return handle;
    }

    /// <summary>截取整个窗口。失败/最小化返回 null。调用方负责 Dispose。</summary>
    public Bitmap? CaptureWindow(IntPtr hwnd)
    {
        if(hwnd == IntPtr.Zero) return null;
        if(!GetWindowRect(hwnd, out var rect)) return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if(width <=0 || height <= 0) return null;

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using(var g = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if(!ok){bitmap.Dispose(); return null;}
        }
        return bitmap;
    }

    /// <summary>
    /// 按 0~1 的比例裁剪出子区域（裁掉无关 UI）。比例为经验值，需按实际窗口微调。
    /// 调用方负责 Dispose。
    /// </summary>
    public Bitmap Crop(Bitmap full, double left, double top, double right,double bottom)
    {
        int x = (int)(full.Width * left);
        int y = (int)(full.Height * top);
        int w = (int)(full.Width * right) - x;
        int h = (int)(full.Height * bottom) - y;

        x = Math.Clamp(x, 0, full.Width - 1);
        y = Math.Clamp(y, 0, full.Height - 1);
        w = Math.Clamp(w, 1, full.Width - x);
        h = Math.Clamp(h, 1, full.Height - y);

        var cropped = new Bitmap(w,h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(full, new Rectangle(0,0,w,h),new Rectangle(x,y,w,h),GraphicsUnit.Pixel);
        return cropped;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }


}
