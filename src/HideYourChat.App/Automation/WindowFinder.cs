using System.Text;

namespace HideYourChat.App.Automation;

public sealed record FoundWindow(
    IntPtr Handle,
    string Title,
    string ClassName);

public static class WindowFinder
{
    public static IReadOnlyList<FoundWindow> FindByTitleKeyword(string keyword)
    {
        var result = new List<FoundWindow>();

        if(string.IsNullOrWhiteSpace(keyword)) return result;

        Win32Native.EnumWindows((hwnd, _) =>
        {
            if(!Win32Native.IsWindowVisible(hwnd)) return true;

            var titleBuilder = new StringBuilder(512);// 预分配512字符缓冲区
            // Windows API 需要固定缓冲区来写入字符串
            // 不能直接返回 string，因为那需要分配内存（C/C++ 风格）
            Win32Native.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString();

            if(string.IsNullOrWhiteSpace(title)) return true;
            if(!title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;

            var classBuilder = new StringBuilder(256);
            Win32Native.GetClassName(hwnd,classBuilder,classBuilder.Capacity);

            result.Add(new FoundWindow(
                hwnd,
                title,
                classBuilder.ToString()
            ));

            return true;
        }, IntPtr.Zero);

        return result;
    }
}