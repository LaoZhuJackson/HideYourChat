using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace HideYourChat.App.Imaging;

public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private readonly PaddleOcrAll? _ocr;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;

    public PaddleOcrEngine()
    {
        PaddleOcrAll? built = null;
        using var ready = new ManualResetEventSlim(false);

        _worker = new Thread(() =>
        {
            // 模型必须在这个专用线程上构造,之后所有 Run 也在这个线程
            try
            {
                built = new PaddleOcrAll(LocalFullModels.ChineseV5, PaddleDevice.Mkldnn())
                {
                    AllowRotateDetection = false,
                    Enable180Classification = false
                };
            }
            catch { built = null; }
            ready.Set();

            // 永远在本线程消费任务队列
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                job();
            }
        })
        { IsBackground = true, Name = "PaddleOCR" };

        _worker.Start();
        ready.Wait();      // 等模型在专用线程上构造完成
        _ocr = built;
    }

    public bool IsAvailable => _ocr is not null;

    public Task<IReadOnlyList<OcrLine>> RecognizeAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        if (_ocr is null) return Task.FromResult<IReadOnlyList<OcrLine>>([]);

        var tcs = new TaskCompletionSource<IReadOnlyList<OcrLine>>();
        // 把这帧的识别工作排到专用线程,而不是 Task.Run(线程池)
        _queue.Add(() =>
        {
            try
            {
                using Mat src = ImageConvert.BitmapToMat(bitmap);
                PaddleOcrResult result = _ocr.Run(src);

                var lines = new List<OcrLine>(result.Regions.Length);
                foreach (var region in result.Regions)
                {
                    var text = (region.Text ?? string.Empty).Trim();
                    if (text.Length == 0) continue;
                    Rect box = region.Rect.BoundingRect();
                    lines.Add(new OcrLine(text, new RectangleF(box.X, box.Y, box.Width, box.Height)));
                }
                tcs.SetResult(lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left).ToList());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, ct);

        return tcs.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(2000);
        _ocr?.Dispose();
        _queue.Dispose();
    }
}