using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 基于 Sdcb.PaddleOCR 的实现。中文精度优于系统 OCR，但首帧要加载模型、单帧较慢。
/// PaddleOcrAll 不是线程安全的，且构造昂贵，所以这里做成「懒加载 + 串行调用」。
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private readonly PaddleOcrAll? _ocr;
    // PaddleOcrAll.Run 是同步且非线程安全的，用信号量保证一次只跑一帧
    private readonly SemaphoreSlim _gate = new(1,1); //限制同一时刻只能有 1 个线程进入某段代码

    public PaddleOcrEngine()
    {
        try
        {
            FullOcrModel model = LocalFullModels.ChineseV5;
            _ocr = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false, //不允许检测带角度的文本，都是水平的
                Enable180Classification = false //没有倒置的存在
            };
        }
        catch
        {
            // 模型/原生 dll 缺失等情况，标记为不可用，让上层回退
            _ocr = null;
        }
    }

    public bool IsAvailable => _ocr is not null;

    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        Bitmap bitmap, CancellationToken cancellationToken = default
    )
    {
        if(_ocr is null) return [];
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                using Mat src = BitmapToMat(bitmap);
                PaddleOcrResult result = _ocr.Run(src);

                var lines = new List<OcrLine>(result.Regions.Length);
                foreach (PaddleOcrResultRegion region in result.Regions)
                {
                    var text = (region.Text ?? string.Empty).Trim();
                    if(text.Length == 0) continue;
                    // region.Rect 是 RotatedRect（带角度），转成轴对齐外接矩形给 OcrLine
                    Rect box = region.Rect.BoundingRect();
                    var bounds = new RectangleF(box.X, box.Y, box.Width, box.Height);
                    lines.Add(new OcrLine(text, bounds));
                }
                // PaddleOCR 的 Regions 顺序不保证从上到下，聊天要按阅读顺序排
                return (IReadOnlyList<OcrLine>)lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left).ToList();
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
    /// <summary>
    /// System.Drawing.Bitmap → OpenCvSharp.Mat。
    /// 走内存 PNG 编解码，避免再引入 OpenCvSharp4.Extensions 包。
    /// </summary>
    private static Mat BitmapToMat(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }

    public void Dispose()
    {
        _ocr?.Dispose();
        _gate.Dispose();
    }
}