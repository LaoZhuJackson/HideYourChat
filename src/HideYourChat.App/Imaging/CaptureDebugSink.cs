using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 调试用：把截图保存到 %TEMP%/HideYourChat/captures/，方便肉眼核对裁剪是否准确。
/// 生产环境把 Enabled 关掉即可。
/// </summary>
public sealed class CaptureDebugSink
{
    public bool Enabled {get; set;} = true;
    public string Folder {get;} = Path.Combine(Path.GetTempPath(), "HideYourChat", "captures");

    /// <summary>保存一张图，返回文件路径（未启用时返回 null）。</summary>
    public string? Save(Bitmap bitmap, string tag)
    {
        if(!Enabled) return null;

        Directory.CreateDirectory(Folder);
        var name = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{tag}.png";
        var path = Path.Combine(Folder,name);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
        return path;
    }
    /// <summary>在资源管理器里打开截图文件夹。</summary>
    public void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", Folder) { UseShellExecute = true });
    }

    /// <summary>把 OCR 结果的边界框画到图上,返回新图(原图不变)。</summary>
    public Bitmap DrawBoxes(Bitmap source, IReadOnlyList<OcrLine> lines)
    {
        var annotated = new Bitmap(source.Width, source.Height, source.PixelFormat);
        using var g = Graphics.FromImage(annotated);
        g.DrawImage(source, 0, 0); // 先把原图画上去

        using var pen = new Pen(Color.Lime, 2); // 绿色框,2px 粗
        using var font = new Font("Microsoft YaHei", 10);
        using var brush = new SolidBrush(Color.Red);

        foreach (var line in lines)
        {
            // 画矩形框
            g.DrawRectangle(pen,
                line.Bounds.X, line.Bounds.Y,
                line.Bounds.Width, line.Bounds.Height);

            // 可选:在框左上角标注文本(方便核对)
            g.DrawString(line.Text, font, brush, line.Bounds.Location);
        }

        return annotated;
    }
}