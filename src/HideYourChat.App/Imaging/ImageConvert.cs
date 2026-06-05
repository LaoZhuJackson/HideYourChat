using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 图像格式转换工具。走内存 PNG 编解码,避免引入 OpenCvSharp4.Extensions 包。
/// PaddleOcrEngine 和 FrameChangeDetector 共用。
/// </summary>
public static class ImageConvert
{
    /// <summary>System.Drawing.Bitmap → OpenCvSharp.Mat(BGR 三通道)。调用方负责 Dispose 返回的 Mat。</summary>
    public static Mat BitmapToMat(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }
}