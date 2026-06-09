using System.Drawing;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 从聊天截图里采样消息气泡的背景色。
/// 优先采"文字左侧的气泡内边距"(无文字、纯背景),失败则回退区域中位数。
/// </summary>
public static class BubbleColorSampler
{
    /// <summary>
    /// 采样某条消息气泡的代表色。
    /// </summary>
    /// <param name="image">聊天区裁剪图(Bounds 的坐标系)。</param>
    /// <param name="bounds">该消息文字的边界框。</param>
    public static Color Sample(Bitmap image, RectangleF bounds)
    {
        // 在文字行垂直中线、文字左边缘左侧 6px 处取一点 —— 气泡内边距,通常无文字
        int probeY = (int)(bounds.Top + bounds.Height / 2);
        int probeX = (int)(bounds.Left - 6);

        probeY = Math.Clamp(probeY, 0, image.Height - 1);

        // 左侧采样点若越界，改成区域中位数兜底
        if(probeX >= 0 && probeX < image.Width) return image.GetPixel(probeX, probeY);

        return MedianColor(image, bounds);
    }

    /// <summary>区域中位数颜色:文字是少数,中位数≈气泡背景。子采样提速。</summary>
    private static Color MedianColor(Bitmap image, RectangleF bounds)
    {
        int x0 = Math.Clamp((int)bounds.Left, 0, image.Width - 1);
        int y0 = Math.Clamp((int)bounds.Top, 0, image.Height - 1);
        int x1 = Math.Clamp((int)bounds.Right, 0, image.Width - 1);
        int y1 = Math.Clamp((int)bounds.Bottom, 0, image.Height - 1);

        var rs = new List<int>();
        var gs = new List<int>();
        var bs = new List<int>();

        // 没隔3px采一个点
        for(int y = y0; y <= y1; y += 3)
            for(int x = x0; x <= x1; x += 3)
            {
                var c = image.GetPixel(x, y);
                rs.Add(c.R); gs.Add(c.G); bs.Add(c.B);
            }
        if(rs.Count == 0) return Color.Gray;

        rs.Sort(); gs.Sort(); bs.Sort();
        int mid = rs.Count / 2;
        return Color.FromArgb(rs[mid], gs[mid], bs[mid]);
    }
}