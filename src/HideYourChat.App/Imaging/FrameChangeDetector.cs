using OpenCvSharp;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 帧变化检测:把图缩成小灰度图,和上一帧比平均像素差。
/// 差异低于阈值就认为"画面没变",可跳过 OCR。
/// </summary>
public sealed class FrameChangeDetector
{
    private Mat? _previous;
    private readonly int _size;
    private readonly double _threshold;

    /// <param name="size">缩略图边长,越小越不敏感。32 是个不错的起点。</param>
    /// <param name="threshold">平均像素差阈值(0~255),越大越不敏感。建议 2~5 之间调。</param>
    public FrameChangeDetector(int size = 32, double threshold = 3.0)
    {
        _size = size;
        _threshold = threshold;
    }

    /// <summary>传入当前帧,返回 true 表示"画面有变化,需要跑 OCR"。</summary>
      public bool HasChanged(Mat current)
    {
        using var gray = new Mat(); // using关键字确保方法结束时会自动调用 .Dispose() 释放 Mat
        Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
        using var small = new Mat();
        Cv2.Resize(gray, small, new OpenCvSharp.Size(_size, _size));

        if(_previous is null)
        {
            _previous = small.Clone(); // 第一帧:没有基准,当作"有变化"
            return true;
        }

        // 计算两帧缩略图的平均绝对差
        using var diff = new Mat();
        Cv2.Absdiff(_previous, small, diff);
        Scalar meanDiff = Cv2.Mean(diff);

        bool changed = meanDiff.Val0 > _threshold;
        if (changed)
        {
            _previous.Dispose();
            _previous = small.Clone(); // 只在判定为"变化"时更新基准
        }
        return changed;
    }

    public void Reset()
    {
        _previous?.Dispose();
        _previous = null;
    }
}